using System.Diagnostics;
using System.Runtime.InteropServices;
using SectorWorks.Core;

namespace SectorWorks.Platform;

public static class CdAudioOutput
{
    public static ICdAudioOutput Open()
    {
        try
        {
            if (OperatingSystem.IsLinux()) { return new AlsaOutput(); }
            if (OperatingSystem.IsWindows()) { return new WaveOutput(); }
        }
        catch (DllNotFoundException error)
        { throw new NotSupportedException("CD audio needs the host ALSA library on Linux or Windows waveform audio.", error); }
        throw new PlatformNotSupportedException("No CD audio backend is available for this OS.");
    }

    private sealed class AlsaOutput : ICdAudioOutput
    {
        private nint handle;
        private long submitted;
        private long played;
        private const int TryAgain = -11;

        internal AlsaOutput()
        {
            Check(OpenPcm(out handle, "default", 0, 1));
            try
            {
                // Interleaved signed 16-bit LE, stereo, 44.1 kHz; no endpoint settings are changed.
                Check(SetParams(handle, 2, 3, 2, 44100, 1, 100000));
                Check(NonBlock(handle, 1));
            }
            catch { Dispose(); throw; }
        }

        public long PlayedFrames
        {
            get
            {
                if (Delay(handle, out nint delay) >= 0)
                { played = Math.Max(played, submitted - Math.Max(0L, (long)delay)); }
                return played;
            }
        }

        public void Write(byte[] pcm, int count, CancellationToken token)
        {
            GCHandle pin = GCHandle.Alloc(pcm, GCHandleType.Pinned);
            try
            {
                int frames = count / 4;
                var timer = Stopwatch.StartNew();
                for (int sent = 0; sent < frames;)
                {
                    token.ThrowIfCancellationRequested();
                    long written = WriteFrames(handle, pin.AddrOfPinnedObject() + sent * 4, (nuint)(frames - sent));
                    if (written == TryAgain || written == 0)
                    {
                        if (timer.ElapsedMilliseconds > 5000) { throw new IOException("The Linux audio device stopped accepting samples."); }
                        int waited = Wait(handle, 20);
                        if (waited < 0 && waited != TryAgain) { RecoverFrom(waited); }
                        continue;
                    }
                    if (written < 0)
                    {
                        if (timer.ElapsedMilliseconds > 5000) { Check(checked((int)written)); }
                        RecoverFrom(checked((int)written));
                        continue;
                    }
                    sent += checked((int)written);
                    submitted += written;
                    timer.Restart();
                }
            }
            finally { pin.Free(); }
        }

        private void RecoverFrom(int status)
        {
            Check(Recover(handle, status, 1));
            // Any frames queued before an underrun were consumed or discarded.
            played = submitted;
        }

        public void Drain(CancellationToken token)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                token.ThrowIfCancellationRequested();
                int status = DrainPcm(handle);
                if (status != TryAgain) { Check(status); played = submitted; return; }
                if (timer.ElapsedMilliseconds > 5000) { throw new IOException("The Linux audio device did not drain."); }
                token.WaitHandle.WaitOne(10);
            }
        }

        public void Dispose()
        {
            if (handle == 0) { return; }
            _ = Drop(handle);
            _ = Close(handle);
            handle = 0;
        }

        private static void Check(int status)
        {
            if (status < 0)
            { throw new IOException("Linux audio: " + (Marshal.PtrToStringUTF8(ErrorText(status)) ?? status.ToString())); }
        }

        private const string Alsa = "libasound.so.2";
        [DllImport(Alsa, EntryPoint = "snd_pcm_open")]
        private static extern int OpenPcm(out nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int stream, int mode);
        [DllImport(Alsa, EntryPoint = "snd_pcm_set_params")]
        private static extern int SetParams(nint handle, int format, int access, uint channels, uint rate, int resample, uint latency);
        [DllImport(Alsa, EntryPoint = "snd_pcm_nonblock")]
        private static extern int NonBlock(nint handle, int flags);
        [DllImport(Alsa, EntryPoint = "snd_pcm_writei")]
        private static extern nint WriteFrames(nint handle, nint bytes, nuint frames);
        [DllImport(Alsa, EntryPoint = "snd_pcm_wait")]
        private static extern int Wait(nint handle, int timeout);
        [DllImport(Alsa, EntryPoint = "snd_pcm_recover")]
        private static extern int Recover(nint handle, int error, int silent);
        [DllImport(Alsa, EntryPoint = "snd_pcm_delay")]
        private static extern int Delay(nint handle, out nint frames);
        [DllImport(Alsa, EntryPoint = "snd_pcm_drain")]
        private static extern int DrainPcm(nint handle);
        [DllImport(Alsa, EntryPoint = "snd_pcm_drop")]
        private static extern int Drop(nint handle);
        [DllImport(Alsa, EntryPoint = "snd_pcm_close")]
        private static extern int Close(nint handle);
        [DllImport(Alsa, EntryPoint = "snd_strerror")]
        private static extern nint ErrorText(int error);
    }

    private sealed class WaveOutput : ICdAudioOutput
    {
        private const int ChunkBytes = 16384;
        private const uint Done = 1;
        private static readonly int HeaderSize = Marshal.SizeOf<Header>();
        private static readonly int FlagsOffset = Marshal.OffsetOf<Header>(nameof(Header.Flags)).ToInt32();
        private static readonly int LengthOffset = Marshal.OffsetOf<Header>(nameof(Header.Length)).ToInt32();
        private readonly List<Buffer> buffers = [];
        private nint handle;
        private int next;
        private long submitted;

        internal WaveOutput()
        {
            var format = new WaveFormat
            { Format = 1, Channels = 2, Rate = 44100, ByteRate = 176400, BlockAlign = 4, Bits = 16 };
            Check(OpenWave(out handle, uint.MaxValue, ref format, 0, 0, 0));
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    var buffer = new Buffer(Marshal.AllocHGlobal(ChunkBytes));
                    buffers.Add(buffer);
                    buffer.Header = Marshal.AllocHGlobal(HeaderSize);
                    Marshal.StructureToPtr(new Header { Data = buffer.Data, Length = ChunkBytes }, buffer.Header, false);
                    Check(Prepare(handle, buffer.Header, HeaderSize));
                    buffer.Prepared = true;
                }
            }
            catch { Dispose(); throw; }
        }

        public long PlayedFrames
        {
            get
            {
                var time = new WaveTime { Type = 2 };
                Check(GetPosition(handle, ref time, Marshal.SizeOf<WaveTime>()));
                return Math.Clamp(time.Type switch
                {
                    2 => time.Value, // TIME_SAMPLES
                    4 => time.Value / 4L, // TIME_BYTES
                    1 => time.Value * 44100L / 1000, // TIME_MS
                    _ => throw new IOException("Windows audio returned an unsupported playback position unit.")
                }, 0, submitted);
            }
        }

        public void Write(byte[] pcm, int count, CancellationToken token)
        {
            if ((uint)count > ChunkBytes) { throw new ArgumentOutOfRangeException(nameof(count)); }
            Buffer buffer = buffers[next];
            WaitFor(buffer, token);
            Marshal.Copy(pcm, 0, buffer.Data, count);
            Marshal.WriteInt32(buffer.Header, LengthOffset, count);
            Check(WriteWave(handle, buffer.Header, HeaderSize));
            buffer.Queued = true;
            submitted += count / 4;
            next = (next + 1) % buffers.Count;
        }

        private static void WaitFor(Buffer buffer, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!buffer.Queued) { return; }
            var timer = Stopwatch.StartNew();
            while ((Marshal.ReadInt32(buffer.Header, FlagsOffset) & Done) == 0)
            {
                token.ThrowIfCancellationRequested();
                if (timer.ElapsedMilliseconds > 5000) { throw new IOException("The Windows audio device did not return a playback buffer."); }
                token.WaitHandle.WaitOne(6);
            }
            buffer.Queued = false;
        }

        public void Drain(CancellationToken token)
        {
            foreach (Buffer buffer in buffers) { WaitFor(buffer, token); }
        }

        public void Dispose()
        {
            if (handle == 0) { return; }
            _ = Reset(handle);
            foreach (Buffer buffer in buffers)
            {
                // If the driver still owns a buffer, do not release memory it may reference.
                if (buffer.Prepared && Unprepare(handle, buffer.Header, HeaderSize) != 0) { continue; }
                if (buffer.Header != 0) { Marshal.FreeHGlobal(buffer.Header); }
                Marshal.FreeHGlobal(buffer.Data);
            }
            _ = Close(handle);
            handle = 0;
        }

        private sealed class Buffer(nint data)
        {
            public nint Data { get; } = data;
            public nint Header { get; set; }
            public bool Prepared { get; set; }
            public bool Queued { get; set; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct WaveFormat
        {
            internal ushort Format, Channels;
            internal uint Rate, ByteRate;
            internal ushort BlockAlign, Bits, Extra;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Header
        {
            internal nint Data;
            internal uint Length, Recorded;
            internal nuint User;
            internal uint Flags, Loops;
            internal nint Next;
            internal nuint Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WaveTime { internal uint Type, Value, Padding; }

        private static void Check(uint status)
        {
            if (status != 0) { throw new IOException($"Windows audio device error {status}."); }
        }

        private const string Winmm = "winmm.dll";
        [DllImport(Winmm, EntryPoint = "waveOutOpen")]
        private static extern uint OpenWave(out nint handle, uint device, ref WaveFormat format, nint callback, nint instance, uint flags);
        [DllImport(Winmm, EntryPoint = "waveOutPrepareHeader")]
        private static extern uint Prepare(nint handle, nint header, int size);
        [DllImport(Winmm, EntryPoint = "waveOutWrite")]
        private static extern uint WriteWave(nint handle, nint header, int size);
        [DllImport(Winmm, EntryPoint = "waveOutGetPosition")]
        private static extern uint GetPosition(nint handle, ref WaveTime position, int size);
        [DllImport(Winmm, EntryPoint = "waveOutReset")]
        private static extern uint Reset(nint handle);
        [DllImport(Winmm, EntryPoint = "waveOutUnprepareHeader")]
        private static extern uint Unprepare(nint handle, nint header, int size);
        [DllImport(Winmm, EntryPoint = "waveOutClose")]
        private static extern uint Close(nint handle);
    }
}
