param([string]$PrivateTarget)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'Test-PublicTree.ps1')
    if (@(git status --porcelain).Count) { throw 'Commit the reviewed source first. Releases must come from a clean Git worktree.' }
    $commit = (git rev-parse HEAD).Trim()
    [xml]$project = Get-Content .\PaperProgress\PaperProgress.csproj
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version.' }
    dotnet run --project .\tests\PaperProgress.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $build = Join-Path $root ('artifacts\build-' + [guid]::NewGuid().ToString('N'))
    $package = Join-Path $build 'package'
    $appFolder = Join-Path $package "versions\$version"
    $publish = Join-Path $build 'dotnet-publish'
    [IO.Directory]::CreateDirectory($appFolder) | Out-Null
    dotnet publish .\PaperProgress\PaperProgress.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    Copy-Item -LiteralPath (Join-Path $publish 'PaperProgress.exe') -Destination $appFolder
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $launcher = Join-Path $package 'PaperProgress.Launcher.exe'
    & $compiler /nologo /target:winexe /optimize+ "/out:$launcher" /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll /win32icon:PaperProgress\Assets\app.ico .\Launcher\Program.cs
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    & (Join-Path $PSScriptRoot 'Test-Launcher.ps1') -Launcher $launcher
    $exe = Join-Path $appFolder 'PaperProgress.exe'
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $channel = @{ version=$version; exePath="versions/$version/PaperProgress.exe"; sha256=$hash; length=(Get-Item -LiteralPath $exe).Length }
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $package 'channel.json'), ($channel | ConvertTo-Json), $encoding)
    Copy-Item -LiteralPath '.\LICENSE','.\README.md' -Destination $package
    [IO.File]::WriteAllText((Join-Path $package 'build-info.json'), (@{ version=$version; commit=$commit; sha256=$hash } | ConvertTo-Json), $encoding)
    $destination = Join-Path $root "artifacts\release-$version"
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $sourceArchive = Join-Path $destination "PaperProgress-$version-source.zip"
    git archive --format=zip "--output=$sourceArchive" HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Source export failed.' }
    $appArchive = Join-Path $destination "PaperProgress-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $appArchive -Force
    Copy-Item -LiteralPath (Join-Path $package 'build-info.json') -Destination $destination
    if ($PrivateTarget) {
        $target = [IO.Path]::GetFullPath($PrivateTarget)
        if ($target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $target -eq $root) { throw 'The personal installation must be outside the source repository.' }
        $versionFolder = Join-Path $target "versions\$version"
        [IO.Directory]::CreateDirectory($versionFolder) | Out-Null
        $deployed = Join-Path $versionFolder 'PaperProgress.exe'
        if (Test-Path -LiteralPath $deployed) { if ((Get-FileHash -LiteralPath $deployed).Hash.ToLowerInvariant() -ne $hash) { throw 'This version already exists with different bytes. Bump the version instead of overwriting it.' } }
        else { Copy-Item -LiteralPath $exe -Destination $deployed }
        Copy-Item -LiteralPath $launcher -Destination (Join-Path $target 'PaperProgress.Launcher.exe') -Force
        $manifestPath = Join-Path $target 'channel.json'
        $temporary = $manifestPath + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
        [IO.File]::WriteAllText($temporary, ($channel | ConvertTo-Json), $encoding)
        if (Test-Path -LiteralPath $manifestPath) { [IO.File]::Replace($temporary, $manifestPath, $null) } else { [IO.File]::Move($temporary, $manifestPath) }
        # Deliberately never copy, replace, enumerate or package the target data folder.
        Write-Output "Private app deployed: $target (data untouched)."
    }
    Write-Output "Version $version; commit $commit; SHA256 $hash"
    Write-Output "Curated public artifacts: $destination"
} finally { Pop-Location }
