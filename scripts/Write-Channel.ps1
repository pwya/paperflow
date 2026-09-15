param([Parameter(Mandatory=$true)][string]$Path, [Parameter(Mandatory=$true)][string]$Content)
$ErrorActionPreference = 'Stop'
$destination = [IO.Path]::GetFullPath($Path)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination)) | Out-Null
$temporary = $destination + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
try {
    [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($false))
    # Windows PowerShell converts a null string argument to an empty path. Use a
    # concrete backup path, which also preserves the previous deployment pointer.
    if ([IO.File]::Exists($destination)) { [IO.File]::Replace($temporary, $destination, $destination + '.previous') }
    else { [IO.File]::Move($temporary, $destination) }
} finally { if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) } }
