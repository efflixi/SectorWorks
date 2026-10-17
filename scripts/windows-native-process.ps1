function Invoke-SectorWorksNativeProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string] $Executable,
        [Parameter(Mandatory = $true)][string] $Arguments,
        [ValidateRange(1, 120000)][int] $TimeoutMilliseconds = 120000
    )
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $Executable
    $start.Arguments = $Arguments
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        if (!$process.Start()) { throw 'Native process did not start.' }
        # Retain the process handle before an early exit; Start-Process can lose
        # the exit status on Windows PowerShell 5.1 after handle disposal.
        $handle = $process.Handle
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = !$process.WaitForExit($TimeoutMilliseconds)
        if ($timedOut) {
            $process.Kill()
            if (!$process.WaitForExit(5000)) { throw 'Timed-out native process could not be stopped.' }
        }
        if (!$stdout.Wait(5000) -or !$stderr.Wait(5000)) { throw 'Native process output did not close within deadline.' }
        $exitCode = $process.ExitCode
        if ($null -eq $exitCode) { throw 'Native process returned no exit status.' }
        return [pscustomobject]@{
            ExitCode = [int]$exitCode
            TimedOut = $timedOut
            Stdout = $stdout.Result
            Stderr = $stderr.Result
        }
    } finally {
        $process.Dispose()
    }
}
