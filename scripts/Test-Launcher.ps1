param([Parameter(Mandatory=$true)][string]$Launcher)
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('PaperProgress-launcher-test-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $root 'package'
$cache = Join-Path $root 'cache'
$appFolder = Join-Path $package 'versions/1.0.0'
[IO.Directory]::CreateDirectory($appFolder) | Out-Null
$synthetic = Join-Path $appFolder 'PaperProgress.exe'
[IO.File]::WriteAllText($synthetic, 'Synthetic executable bytes; check-only mode never executes them.')
$channel = @{ version='1.0.0'; exePath='versions/1.0.0/PaperProgress.exe'; sha256=(Get-FileHash $synthetic).Hash.ToLowerInvariant(); length=(Get-Item $synthetic).Length }
$manifest = Join-Path $package 'channel.json'
$utf8 = [Text.UTF8Encoding]::new($false)
function Write-Channel { [IO.File]::WriteAllText($manifest, ($channel | ConvertTo-Json), $utf8) }
function Check-Launch([string]$case) {
    $report = Join-Path $root ($case + '.txt')
    $arguments = '--package-dir "' + $package + '" --cache-dir "' + $cache + '" --check-only "' + $report + '"'
    $process = Start-Process -FilePath $Launcher -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id; throw "Launcher check timed out: $case" }
    $lines = [IO.File]::ReadAllLines($report)
    if ($lines[0] -ne '1.0.0' -or -not (Test-Path -LiteralPath $lines[1])) { throw "Launcher failed: $case" }
    return $lines
}
Write-Channel
$result = Check-Launch 'hydrate'
if ($result[2] -ne 'OK' -or (Get-FileHash -LiteralPath $result[1]).Hash -ne $channel.sha256) { throw 'Hydration/checksum failed.' }
[IO.File]::WriteAllText($result[1], 'Damaged cache')
$result = Check-Launch 'repair-cache'
if ($result[2] -ne 'OK') { throw 'Cache repair failed.' }
$channel.version = '1.0.1'; $channel.exePath = 'versions/1.0.1/PaperProgress.exe'; Write-Channel
$result = Check-Launch 'missing-update'
if ($result[2] -eq 'OK') { throw 'Missing update did not fall back.' }
$newFolder = Join-Path $package 'versions/1.0.1'; [IO.Directory]::CreateDirectory($newFolder) | Out-Null
[IO.File]::WriteAllText((Join-Path $newFolder 'PaperProgress.exe'), 'Incorrect update bytes')
$result = Check-Launch 'bad-update'
if ($result[2] -eq 'OK') { throw 'Damaged update did not fall back.' }
$channel.exePath = '../outside.exe'; Write-Channel
$result = Check-Launch 'traversal'
if ($result[2] -eq 'OK') { throw 'Invalid path was accepted.' }
[IO.File]::WriteAllText($manifest, '{broken')
$result = Check-Launch 'malformed-manifest'
if ($result[2] -eq 'OK') { throw 'Malformed manifest was accepted.' }
Write-Output 'PASS: 6 launcher scenarios (hydration, repair, missing update, damaged update, path validation, malformed manifest).'
