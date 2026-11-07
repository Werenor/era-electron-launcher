param(
 [Parameter(Mandatory=$true)][string]$Package,
 [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference='Stop'
$packagePath=(Resolve-Path -LiteralPath $Package).Path
$destinationPath=[IO.Path]::GetFullPath($Destination)
if($packagePath.TrimEnd('\') -eq $destinationPath.TrimEnd('\')) {throw 'Source and destination must differ.'}
foreach($file in @('StartGame.exe','KeyStateProbe.exe','patches\keystate.cjs','patches\launcher-window.js')) {
 if(-not(Test-Path -LiteralPath (Join-Path $packagePath $file) -PathType Leaf)){throw "Incomplete package: $file"}
}
$targetExecutable=Join-Path $destinationPath 'StartGame.exe'
$active=@(Get-Process -Name StartGame -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $targetExecutable -or -not $_.Path})
if($active.Count -gt 0){throw 'Close the destination launcher before updating.'}
$files=@(Get-ChildItem -LiteralPath $packagePath -File -Recurse)
# Deploy companion files first and the matching executable last. Preserve games and user data.
$ordered=@($files | Where-Object {$_.FullName -ne (Join-Path $packagePath 'StartGame.exe')})+@((Get-Item -LiteralPath (Join-Path $packagePath 'StartGame.exe')))
foreach($file in $ordered) {
 $relative=$file.FullName.Substring($packagePath.TrimEnd('\').Length+1)
 $target=Join-Path $destinationPath $relative
 New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
 Copy-Item -LiteralPath $file.FullName -Destination $target -Force
}
foreach($file in $files) {
 $relative=$file.FullName.Substring($packagePath.TrimEnd('\').Length+1)
 if((Get-FileHash -LiteralPath $file.FullName).Hash -ne (Get-FileHash -LiteralPath (Join-Path $destinationPath $relative)).Hash){throw "Deployment verification failed: $relative"}
}
Write-Output "Installed and verified all $($files.Count) package files: $destinationPath"
