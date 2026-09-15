param([string]$PrivateTarget)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    & (Join-Path $PSScriptRoot 'Test-PublicTree.ps1')
    if (@(git status --porcelain).Count) { throw 'Commit the reviewed source first. Releases must come from a clean Git worktree.' }
    $commit = (git rev-parse HEAD).Trim()
    $build = Join-Path $root ('artifacts\build-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($build) | Out-Null
    $snapshot = Join-Path $build 'source.zip'
    git archive --format=zip "--output=$snapshot" $commit
    if ($LASTEXITCODE -ne 0) { throw 'Source snapshot failed.' }
    $source = Join-Path $build 'source'
    Expand-Archive -LiteralPath $snapshot -DestinationPath $source
    & (Join-Path $source 'scripts/Test-Channel.ps1')
    # Build the committed snapshot, even if someone edits the worktree during compilation.
    [xml]$project = Get-Content (Join-Path $source 'PaperProgress/PaperProgress.csproj')
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version.' }
    dotnet run --project (Join-Path $source 'tests/PaperProgress.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $package = Join-Path $build 'package'
    $appFolder = Join-Path $package "versions\$version"
    $publish = Join-Path $build 'dotnet-publish'
    [IO.Directory]::CreateDirectory($appFolder) | Out-Null
    dotnet publish (Join-Path $source 'PaperProgress/PaperProgress.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    Copy-Item -LiteralPath (Join-Path $publish 'PaperProgress.exe') -Destination $appFolder
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $launcher = Join-Path $package 'PaperProgress.Launcher.exe'
    & $compiler /nologo /target:winexe /optimize+ "/out:$launcher" /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll ("/win32icon:" + (Join-Path $source 'PaperProgress/Assets/app.ico')) (Join-Path $source 'Launcher/Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    & (Join-Path $source 'scripts/Test-Launcher.ps1') -Launcher $launcher
    $exe = Join-Path $appFolder 'PaperProgress.exe'
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $channel = @{ version=$version; exePath="versions/$version/PaperProgress.exe"; sha256=$hash; length=(Get-Item -LiteralPath $exe).Length }
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $package 'channel.json'), ($channel | ConvertTo-Json), $encoding)
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE'),(Join-Path $source 'README.md') -Destination $package
    [IO.File]::WriteAllText((Join-Path $package 'build-info.json'), (@{ version=$version; commit=$commit; sha256=$hash } | ConvertTo-Json), $encoding)
    $destination = Join-Path $root "artifacts\release-$version"
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $sourceArchive = Join-Path $destination "PaperProgress-$version-source.zip"
    Copy-Item -LiteralPath $snapshot -Destination $sourceArchive -Force
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
        & (Join-Path $source 'scripts/Write-Channel.ps1') -Path $manifestPath -Content ($channel | ConvertTo-Json)
        # Deliberately never copy, replace, enumerate or package the target data folder.
        Write-Output "Private app deployed: $target (data untouched)."
    }
    Write-Output "Version $version; commit $commit; SHA256 $hash"
    Write-Output "Curated public artifacts: $destination"
} finally { Pop-Location }
