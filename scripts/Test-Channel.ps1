$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('PaperProgress-channel-' + [guid]::NewGuid().ToString('N'))
$path = Join-Path $root 'channel.json'
$writer = Join-Path $PSScriptRoot 'Write-Channel.ps1'
& $writer -Path $path -Content '{"version":"1.0.0"}'
if ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).version -ne '1.0.0') { throw 'Initial manifest failed.' }
& $writer -Path $path -Content '{"version":"1.1.0"}'
if ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).version -ne '1.1.0') { throw 'Manifest replacement failed.' }
if ((Get-Content -LiteralPath ($path + '.previous') -Raw | ConvertFrom-Json).version -ne '1.0.0') { throw 'Previous manifest not preserved.' }
& $writer -Path $path -Content '{"version":"1.2.0"}'
if ((Get-Content -LiteralPath $path -Raw | ConvertFrom-Json).version -ne '1.2.0' -or (Get-Content -LiteralPath ($path + '.previous') -Raw | ConvertFrom-Json).version -ne '1.1.0') { throw 'Repeated update failed.' }
if (@(Get-ChildItem -LiteralPath $root -Filter '*.tmp').Count) { throw 'Temporary manifests remain.' }
Write-Output 'PASS: manifest creation, replacement, previous-version backup and repeated update.'
