$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot '../../External/Upscalers/Restore.ps1') -VerifyOnly
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$msbuild = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'Install Visual Studio C++ x64 build tools before building reconstruction.' }
$start = [Diagnostics.ProcessStartInfo]::new($msbuild)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.Arguments = '"' + (Join-Path $PSScriptRoot 'Reconstruction.vcxproj') + '" /nologo /v:minimal /p:Configuration=Release /p:Platform=x64'
# Some tool hosts provide both PATH and Path; .NET Framework MSBuild rejects that environment.
$path = $env:Path
foreach ($key in @($start.Environment.Keys)) {
    if ($key -ieq 'path') { [void]$start.Environment.Remove($key) }
}
$start.Environment['Path'] = $path
$process = [Diagnostics.Process]::Start($start)
$stdout = $process.StandardOutput.ReadToEndAsync()
$stderr = $process.StandardError.ReadToEndAsync()
$process.WaitForExit()
Write-Output $stdout.Result
Write-Output $stderr.Result
if ($process.ExitCode -ne 0) { throw 'Reconstruction native build failed.' }
