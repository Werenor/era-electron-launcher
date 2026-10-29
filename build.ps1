param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$out = Join-Path $PSScriptRoot 'dist\Era-Electron-Launcher-v0.3.0-win-x64'
New-Item -ItemType Directory -Path $out,(Join-Path $out 'patches') -Force | Out-Null
$native = Join-Path $PSScriptRoot 'native\KeyStateProbe.cs'
& $compiler /nologo /platform:x64 /target:exe /optimize+ "/out:$out\KeyStateProbe.exe" $native
if ($LASTEXITCODE -ne 0) { throw 'Native helper build failed' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'patches\keystate.cjs'),(Join-Path $PSScriptRoot 'patches\launcher-window.js') -Destination (Join-Path $out 'patches') -Force
$helper = (Get-FileHash -LiteralPath (Join-Path $out 'KeyStateProbe.exe')).Hash.ToLowerInvariant()
$keys = (Get-FileHash -LiteralPath (Join-Path $out 'patches\keystate.cjs')).Hash.ToLowerInvariant()
$bridge = (Get-FileHash -LiteralPath (Join-Path $out 'patches\launcher-window.js')).Hash.ToLowerInvariant()
$constants = @"
internal static class BuildInfo {
 internal const string BaseHash = "e0866e01bfe11435cb3acd36a8c963b888fc5cc9918817548844d49b311a98fb";
 internal const string HelperHash = "$helper";
 internal const string KeyStateHash = "$keys";
 internal const string BridgeHash = "$bridge";
 internal static readonly EngineMode[] Modes = {
  new EngineMode { Id="standard", Name="标准输入（原版）", KeyState=false },
  new EngineMode { Id="keystate", Name="实时按键 · keystate 1.1.0", KeyState=true }
 };
}
"@
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'BuildInfo.cs'), $constants, [Text.UTF8Encoding]::new($false))
$sources = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /platform:x64 /target:winexe /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$out\StartGame.exe" $sources
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
foreach ($file in @('README.md','LICENSE','THIRD_PARTY_NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $out $file) -Force }
$report = Join-Path $PSScriptRoot 'dist\self-test.txt'
$test = Start-Process -FilePath (Join-Path $out 'StartGame.exe') -ArgumentList @('--self-test', $report) -WindowStyle Hidden -PassThru -Wait
if ($test.ExitCode -ne 0) { Get-Content -LiteralPath $report; throw 'Launcher checks failed' }
Get-Content -LiteralPath $report
$zip = Join-Path $PSScriptRoot 'dist\Era-Electron-Launcher-v0.3.0-win-x64.zip'
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -Force
$hash = (Get-FileHash -LiteralPath $zip).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'dist\SHA256SUMS.txt'), "$hash  Era-Electron-Launcher-v0.3.0-win-x64.zip`n", [Text.UTF8Encoding]::new($false))
Write-Output "Built: $zip"
