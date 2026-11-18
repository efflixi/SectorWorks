<#
.SYNOPSIS
Owner-only, process-scoped SeBackupPrivilege helper for the bounded R7 child.
.DESCRIPTION
Never invoke through SSH. Does not grant privileges to an account or enable
SeRestorePrivilege. Requires an already elevated administrator token containing
disabled SeBackupPrivilege. Dispose restores and verifies its original state.
The R7 driver bounds/kills its isolated child; no interactive-shell token changes.
#>
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Owner must launch the backup-read scope locally as administrator; SSH remains standard.'
}
$backupApi = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
public sealed class SectorWorksOwnerBackupReadScope : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern void SetLastError(uint error);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValueW(string system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(IntPtr token, uint type, IntPtr buffer, uint size, out uint needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, [MarshalAs(UnmanagedType.Bool)] bool disableAll,
        IntPtr state, uint previousBytes, IntPtr previous, out uint returned);
    private IntPtr token = IntPtr.Zero;
    private long luid;
    private uint original;
    private bool attempted;
    public bool Restored { get; private set; }
    private static void Check(bool ok) { if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    private uint ReadAttributes()
    {
        uint needed;
        bool first = GetTokenInformation(token, 3, IntPtr.Zero, 0, out needed);
        if (first || Marshal.GetLastWin32Error() != 122 || needed < 4 || needed > 65536)
            throw new InvalidOperationException("Unexpected token buffer bound.");
        IntPtr buffer = Marshal.AllocHGlobal((int)needed);
        try {
            uint returned; Check(GetTokenInformation(token, 3, buffer, needed, out returned));
            uint count = (uint)Marshal.ReadInt32(buffer);
            if (returned > needed || count > 512 || 4UL + 12UL * count > returned)
                throw new InvalidOperationException("Invalid token privilege layout.");
            for (uint i = 0; i < count; ++i) {
                int offset = checked(4 + (int)i * 12);
                if (Marshal.ReadInt64(buffer, offset) == luid) return (uint)Marshal.ReadInt32(buffer, offset + 8);
            }
            throw new InvalidOperationException("Existing owner token lacks SeBackupPrivilege; no grant is attempted.");
        } finally { Marshal.FreeHGlobal(buffer); }
    }
    private void SetAttributes(uint attributes)
    {
        // TOKEN_PRIVILEGES with one entry: DWORD count, 8-byte LUID at 4,
        // DWORD attributes at 12. Avoid CLR 8-byte struct alignment padding.
        IntPtr state = Marshal.AllocHGlobal(16);
        try {
            Marshal.WriteInt32(state, 0, 1); Marshal.WriteInt64(state, 4, luid);
            Marshal.WriteInt32(state, 12, unchecked((int)attributes));
            uint returned; SetLastError(0);
            bool ok = AdjustTokenPrivileges(token, false, state, 0, IntPtr.Zero, out returned);
            int error = Marshal.GetLastWin32Error();
            if (!ok || error != 0) throw new Win32Exception(error);
        } finally { Marshal.FreeHGlobal(state); }
    }
    public SectorWorksOwnerBackupReadScope()
    {
        var principal = new WindowsPrincipal(WindowsIdentity.GetCurrent());
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("Owner administrator token required; no standard-account adjustment.");
        Check(OpenProcessToken(GetCurrentProcess(), 0x28, out token)); // QUERY | ADJUST_PRIVILEGES
        try {
            Check(LookupPrivilegeValueW(null, "SeBackupPrivilege", out luid));
            original = ReadAttributes();
            if ((original & 2) != 0) throw new InvalidOperationException("Owner backup privilege must initially be disabled.");
            attempted = true;
            SetAttributes(original | 2);
            if ((ReadAttributes() & 2) == 0) throw new InvalidOperationException("Backup privilege did not enable.");
        } catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (token == IntPtr.Zero) return;
        try {
            if (attempted) {
                SetAttributes(original);
                if (ReadAttributes() != original) throw new InvalidOperationException("Original backup privilege state was not restored.");
                Restored = true;
            }
        } finally { IntPtr closing = token; token = IntPtr.Zero; Check(CloseHandle(closing)); }
    }
}
'@
Add-Type -TypeDefinition $backupApi -ErrorAction Stop
