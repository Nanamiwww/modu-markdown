<#
  Builds the portable reader with the C# compiler that ships with Windows (.NET Framework 4.x),
  then runs the built-in self-test. No SDK, NuGet or network access is needed.

  Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 [-OutputDirectory <dir>] [-SkipTests]
  Output: dist\CodexMarkdownReader.exe, dist\build-log.txt, dist\selftest-report.txt
#>
param(
    [string]$OutputDirectory,
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $root "dist" }
$dist = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$log = Join-Path $dist "build-log.txt"
$report = Join-Path $dist "selftest-report.txt"
if (Test-Path -LiteralPath $report) { Remove-Item -LiteralPath $report -Force }
"Build started $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $log -Encoding utf8

$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
if (-not (Test-Path -LiteralPath $framework)) { $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319" }
$csc = Join-Path $framework "csc.exe"
if (-not (Test-Path -LiteralPath $csc)) {
    "FAILED: C# compiler not found: $csc" | Out-File -FilePath $log -Append -Encoding utf8
    throw "C# compiler not found: $csc"
}
$wpf = Join-Path $framework "WPF"
$assets = Join-Path $root "release-tools"
$sources = Get-ChildItem -LiteralPath (Join-Path $root "src") -Recurse -Filter *.cs | Sort-Object FullName | ForEach-Object { $_.FullName }
$exe = Join-Path $dist "CodexMarkdownReader.exe"

# A running copy (or a scanner) may still hold the previous exe. Windows allows renaming a
# loaded image, so move it aside instead of failing; stale copies are cleaned up next time.
Get-ChildItem -LiteralPath $dist -Filter "CodexMarkdownReader.old-*.exe" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $exe) {
    try { Remove-Item -LiteralPath $exe -Force -ErrorAction Stop }
    catch {
        $stale = Join-Path $dist ("CodexMarkdownReader.old-" + (Get-Date -Format "yyyyMMddHHmmss") + ".exe")
        try {
            Move-Item -LiteralPath $exe -Destination $stale -Force -ErrorAction Stop
            "Previous exe was in use; moved to $stale" | Out-File -FilePath $log -Append -Encoding utf8
        }
        catch {
            "FAILED: $exe is locked. Close all reader windows and build again." | Out-File -FilePath $log -Append -Encoding utf8
            throw "Output file is locked: $exe"
        }
    }
}

$arguments = @(
    "/nologo", "/target:winexe", "/optimize+", "/platform:anycpu", "/codepage:65001", "/warn:3",
    "/out:$exe",
    "/reference:$wpf\PresentationCore.dll", "/reference:$wpf\PresentationFramework.dll", "/reference:$wpf\WindowsBase.dll",
    "/reference:$wpf\System.Printing.dll", "/reference:$wpf\ReachFramework.dll",
    "/reference:$framework\System.Xaml.dll", "/reference:$framework\System.Core.dll", "/reference:$framework\System.dll",
    "/reference:$framework\System.Xml.dll"
)
$icon = Join-Path $assets "CodexMarkdownReader.ico"
$manifest = Join-Path $assets "app.manifest"
if (Test-Path -LiteralPath $icon) { $arguments += "/win32icon:$icon" }
if (Test-Path -LiteralPath $manifest) { $arguments += "/win32manifest:$manifest" }
$arguments += $sources

$output = & $csc $arguments 2>&1
$code = $LASTEXITCODE
$output | Out-File -FilePath $log -Append -Encoding utf8
if ($code -ne 0) {
    "FAILED: compiler exit code $code" | Out-File -FilePath $log -Append -Encoding utf8
    $output | Write-Host
    throw "Build failed with exit code $code (see $log)"
}
"Compiled: $exe" | Out-File -FilePath $log -Append -Encoding utf8

if (-not $SkipTests) {
    $process = Start-Process -FilePath $exe -ArgumentList @("--self-test", "`"$report`"") -WindowStyle Hidden -Wait -PassThru
    "Self-test exit code: $($process.ExitCode)" | Out-File -FilePath $log -Append -Encoding utf8
    if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Encoding UTF8 | Out-File -FilePath $log -Append -Encoding utf8 }
    if ($process.ExitCode -ne 0) {
        "FAILED: self-test" | Out-File -FilePath $log -Append -Encoding utf8
        if (Test-Path -LiteralPath $report) { Get-Content -LiteralPath $report -Encoding UTF8 | Write-Host }
        throw "Self-test failed with exit code $($process.ExitCode) (see $report)"
    }
}
"SUCCESS $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File -FilePath $log -Append -Encoding utf8
Write-Host "Build and self-test passed: $exe"
