param(
  [string]$PrivateTarget,
  # Optional: mirror the finished release to Gitee as well (repo, branches, tags, the
  # version release and the fixed "latest" release the app reads first).
  [switch]$MirrorToGitee,
  [string]$GiteeOwner = '',
  [string]$GiteeRepo = 'paperflow',
  [string]$NotesFile = ''
)
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
    # Windows PowerShell reads BOM-less files as ANSI, which mangles the Chinese product
    # description and breaks the XML cast. Always read project files as UTF-8.
    [xml]$project = Get-Content (Join-Path $source 'PaperFlow/PaperFlow.csproj') -Encoding UTF8
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version.' }
    # Tag the commit locally so GitHub and Gitee end up with the same tag object
    # (gh release create then reuses it instead of inventing its own).
    if (-not (git tag --list "v$version")) { git tag "v$version" $commit }
    elseif ((git rev-list -n 1 "v$version").Trim() -ne $commit) { throw "Tag v$version already points at another commit." }
    dotnet run --project (Join-Path $source 'tests/PaperFlow.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $package = Join-Path $build 'package'
    $appFolder = Join-Path $package "versions\$version"
    $publish = Join-Path $build 'dotnet-publish'
    [IO.Directory]::CreateDirectory($appFolder) | Out-Null
    # SourceRevisionId lands in the assembly informational version, which the About panel shows.
    dotnet publish (Join-Path $source 'PaperFlow/PaperFlow.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -p:ContinuousIntegrationBuild=true -p:SourceRevisionId=$commit -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    Copy-Item -LiteralPath (Join-Path $publish 'PaperFlow.exe') -Destination $appFolder
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $launcher = Join-Path $package 'PaperFlow.Launcher.exe'
    & $compiler /nologo /target:winexe /optimize+ "/out:$launcher" /reference:System.Windows.Forms.dll /reference:System.Runtime.Serialization.dll ("/win32icon:" + (Join-Path $source 'PaperFlow/Assets/app.ico')) (Join-Path $source 'Launcher/Program.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
    & (Join-Path $source 'scripts/Test-Launcher.ps1') -Launcher $launcher
    $exe = Join-Path $appFolder 'PaperFlow.exe'
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    $channel = @{ version=$version; exePath="versions/$version/PaperFlow.exe"; sha256=$hash; length=(Get-Item -LiteralPath $exe).Length }
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText((Join-Path $package 'channel.json'), ($channel | ConvertTo-Json), $encoding)
    Copy-Item -LiteralPath (Join-Path $source 'LICENSE'),(Join-Path $source 'README.md') -Destination $package
    [IO.File]::WriteAllText((Join-Path $package 'build-info.json'), (@{ version=$version; commit=$commit; sha256=$hash } | ConvertTo-Json), $encoding)
    $destination = Join-Path $root "artifacts\release-$version"
    [IO.Directory]::CreateDirectory($destination) | Out-Null
    $sourceArchive = Join-Path $destination "PaperFlow-$version-source.zip"
    Copy-Item -LiteralPath $snapshot -Destination $sourceArchive -Force
    $appArchive = Join-Path $destination "PaperFlow-$version-win-x64.zip"
    Compress-Archive -Path (Join-Path $package '*') -DestinationPath $appArchive -Force
    Copy-Item -LiteralPath (Join-Path $package 'build-info.json') -Destination $destination
    # 程序内自动更新的两样东西：单独的 exe，和一份固定名字的 update.json。
    # 清单里的地址必须是真的 Release 附件地址；程序只会在 `/releases/latest/download/update.json`
    # 读这份清单，所以每个正式版本都要把它一起传上去。
    # 用 -f 拼，不用字符串插值：插值里 $version 后面的变量名容易被读断，1.13.0 就这么错过一次。
    $assetName = 'PaperFlow-{0}-win-x64.exe' -f $version
    $updateUrl = 'https://github.com/pwya/paperflow/releases/download/v{0}/{1}' -f $version, $assetName
    Copy-Item -LiteralPath $exe -Destination (Join-Path $destination $assetName) -Force
    $update = @{
        version = $version
        url = $updateUrl
        sha256 = $hash
        length = (Get-Item -LiteralPath $exe).Length
    }
    [IO.File]::WriteAllText((Join-Path $destination 'update.json'), ($update | ConvertTo-Json), $encoding)
    # 清单是程序内更新唯一的入口，这里逐项对一遍：名字、地址、哈希、大小都要和附件真身一致。
    $written = Get-Content -LiteralPath (Join-Path $destination 'update.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not (Test-Path -LiteralPath (Join-Path $destination $assetName))) { throw 'The standalone executable for in-app updates is missing.' }
    if ($written.version -ne $version -or $written.url -ne $updateUrl -or $written.sha256 -ne $hash) { throw 'The update manifest does not describe the published asset.' }
    if ((Get-Item -LiteralPath (Join-Path $destination $assetName)).Length -ne [long]$written.length) { throw 'The update manifest length does not match the published asset.' }
    if ($PrivateTarget) {
        $target = [IO.Path]::GetFullPath($PrivateTarget)
        if ($target.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or $target -eq $root) { throw 'The personal installation must be outside the source repository.' }
        $versionFolder = Join-Path $target "versions\$version"
        [IO.Directory]::CreateDirectory($versionFolder) | Out-Null
        $deployed = Join-Path $versionFolder 'PaperFlow.exe'
        if (Test-Path -LiteralPath $deployed) { if ((Get-FileHash -LiteralPath $deployed).Hash.ToLowerInvariant() -ne $hash) { throw 'This version already exists with different bytes. Bump the version instead of overwriting it.' } }
        else { Copy-Item -LiteralPath $exe -Destination $deployed }
        Copy-Item -LiteralPath $launcher -Destination (Join-Path $target 'PaperFlow.Launcher.exe') -Force
        $manifestPath = Join-Path $target 'channel.json'
        & (Join-Path $source 'scripts/Write-Channel.ps1') -Path $manifestPath -Content ($channel | ConvertTo-Json)
        # Deliberately never copy, replace, enumerate or package the target data folder.
        Write-Output "Private app deployed: $target (data untouched)."
    }
    Write-Output "Version $version; commit $commit; SHA256 $hash"
    Write-Output "Curated public artifacts: $destination"
    if ($MirrorToGitee) {
        $owner = if ($GiteeOwner) { $GiteeOwner } else { $env:GITEE_OWNER }
        if (-not $owner) { throw 'Pass -GiteeOwner (or set GITEE_OWNER) to mirror to Gitee.' }
        $mirror = Join-Path $source 'scripts/Mirror-Gitee.ps1'
        $mirrorArguments = @{ Source = $root; GiteeOwner = $owner; GiteeRepo = $GiteeRepo; Version = $version; ArtifactsDir = $destination }
        if ($NotesFile) { $mirrorArguments['NotesFile'] = $NotesFile }
        & $mirror @mirrorArguments
        if ($LASTEXITCODE -ne 0) { throw 'Mirroring to Gitee failed.' }
    }
} finally { Pop-Location }
