using DiscUtils;
//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using SectorWorks.NtfsSecurity;
using DiscUtils.Streams;

namespace SectorWorks.ManagedNtfs
{
    internal sealed class SecurityDescriptor : IByteArraySerializable, IDiagnosticTraceable
    {
        public SecurityDescriptor() {}

        public SecurityDescriptor(PortableSecurityDescriptor secDesc)
        {
            Descriptor = secDesc;
        }

        public PortableSecurityDescriptor Descriptor { get; set; }

        public int Size
        {
            get { return Descriptor.ToBinary().Length; }
        }

        public int ReadFrom(byte[] buffer, int offset)
        {
            Descriptor = PortableSecurityDescriptor.ParsePrefix(buffer.AsSpan(offset), out int consumed);
            return consumed;
        }

        public void WriteTo(byte[] buffer, int offset)
        {
            Descriptor.ToBinary().CopyTo(buffer, offset);
        }

        public void Dump(TextWriter writer, string indent)
        {
            writer.WriteLine(indent + "Descriptor (self-relative hex): " + Convert.ToHexString(Descriptor.ToBinary()));
        }

        public uint CalcHash()
        {
            byte[] buffer = new byte[Size];
            WriteTo(buffer, 0);
            return CalcHash(buffer);
        }

        internal static uint CalcHash(byte[] buffer)
        {
            if (buffer.Length % 4 != 0)
            {
                throw new IOException("NTFS security descriptor is not DWORD-aligned.");
            }

            uint hash = 0;
            for (int i = 0; i < buffer.Length / 4; ++i)
            {
                // NTFS defines this hash using modular unsigned 32-bit addition.
                hash = unchecked(EndianUtilities.ToUInt32LittleEndian(buffer, i * 4) + ((hash << 3) | (hash >> 29)));
            }

            return hash;
        }

    }
}