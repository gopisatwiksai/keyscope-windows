[CmdletBinding()]
param(
    [switch]$RunTests,
    [switch]$ProbeLoopback
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root '.build'
$dist = Join-Path $root 'dist'
$package = Join-Path $build 'cscore.1.2.1.2.nupkg'
$packageContents = Join-Path $build 'cscore'
$csCoreDll = Join-Path $packageContents 'lib\net35-client\CSCore.dll'
$nativeKeyFinderDll = Join-Path $root 'native\keyfinder_bridge.dll'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Force -Path $build, $dist | Out-Null

if (-not (Test-Path -LiteralPath $csCoreDll)) {
    Write-Host 'Downloading the free CSCore WASAPI dependency...'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/cscore/1.2.1.2/cscore.1.2.1.2.nupkg' -OutFile $package
    Expand-Archive -LiteralPath $package -DestinationPath $packageContents -Force
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework compiler was not found. Install the free .NET Framework 4.8 Developer Pack, then run this script again.'
}
if (-not (Test-Path -LiteralPath $nativeKeyFinderDll)) {
    throw 'The bundled native whole-track key-analysis engine is missing.'
}

$appSources = @(
    (Join-Path $root 'Program.cs'),
    (Join-Path $root 'MainForm.cs'),
    (Join-Path $root 'MusicAnalysis.cs'),
    (Join-Path $root 'NativeKeyFinderDetector.cs'),
    (Join-Path $root 'AudioLoopbackListener.cs')
)

Write-Host 'Building KeyScope...'
& $compiler /nologo /platform:x86 /target:winexe ("/out:{0}" -f (Join-Path $dist 'KeyScope.exe')) `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll ("/r:{0}" -f $csCoreDll) @appSources
if ($LASTEXITCODE -ne 0) { throw 'The KeyScope build failed.' }

Copy-Item -LiteralPath $csCoreDll -Destination (Join-Path $dist 'CSCore.dll') -Force
Copy-Item -LiteralPath $nativeKeyFinderDll -Destination (Join-Path $dist 'keyfinder_bridge.dll') -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.md') -Destination $dist -Force
Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.md') -Destination $dist -Force

if ($RunTests) {
    $testExe = Join-Path $build 'DetectorSmokeTests.exe'
    Write-Host 'Running synthetic detector checks...'
    & $compiler /nologo /platform:x86 /target:exe ("/out:{0}" -f $testExe) ("/r:{0}" -f $csCoreDll) `
        (Join-Path $root 'MusicAnalysis.cs') `
        (Join-Path $root 'NativeKeyFinderDetector.cs') `
        (Join-Path $root 'AudioLoopbackListener.cs') `
        (Join-Path $root 'tests\SmokeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The smoke-test build failed.' }
    Copy-Item -LiteralPath $csCoreDll -Destination (Join-Path $build 'CSCore.dll') -Force
    Copy-Item -LiteralPath $nativeKeyFinderDll -Destination (Join-Path $build 'keyfinder_bridge.dll') -Force
    & $testExe
    if ($LASTEXITCODE -ne 0) { throw 'The synthetic detector checks failed.' }

    $nativeTestExe = Join-Path $build 'NativeEngineSmokeTests.exe'
    Write-Host 'Running the embedded whole-track key-engine check...'
    & $compiler /nologo /platform:x86 /target:exe ("/out:{0}" -f $nativeTestExe) `
        (Join-Path $root 'MusicAnalysis.cs') `
        (Join-Path $root 'NativeKeyFinderDetector.cs') `
        (Join-Path $root 'tests\NativeEngineSmokeTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The native key-engine smoke-test build failed.' }
    & $nativeTestExe
    if ($LASTEXITCODE -ne 0) { throw 'The native whole-track key-engine check failed.' }
}

if ($ProbeLoopback) {
    $probeExe = Join-Path $build 'LoopbackProbe.exe'
    Write-Host 'Opening Windows WASAPI loopback for two seconds...'
    & $compiler /nologo /platform:x86 /target:exe ("/out:{0}" -f $probeExe) ("/r:{0}" -f $csCoreDll) `
        (Join-Path $root 'tests\LoopbackProbe.cs')
    if ($LASTEXITCODE -ne 0) { throw 'The loopback-probe build failed.' }
    & $probeExe
    if ($LASTEXITCODE -ne 0) { throw 'The WASAPI loopback probe did not receive audio callbacks.' }
}

Write-Host "Built: $(Join-Path $dist 'KeyScope.exe')"
