param(
  # Defaults to the repository this script lives in, so nothing machine-specific is baked in.
  [string]$Source = (Split-Path -Parent $PSScriptRoot),
  [string]$GiteeOwner = '',
  [string]$GiteeRepo = 'paperflow',
  [string]$Version = '',
  [string]$NotesFile = '',
  [string]$ArtifactsDir = '',
  [string]$TokenFile = "$env:USERPROFILE\.gitee-token.txt",
  [switch]$DryRun
)
# Mirror the GitHub repository to Gitee: create the repo if needed, push branches and
# tags, publish a Gitee release with the same assets, and keep a fixed "latest" release
# whose update.json points at the Gitee download - that is the file the app reads first.
#
# The token comes from the GITEE_TOKEN environment variable or from TokenFile (single
# line). It never reaches .git/config or a remote URL: the push uses an askpass helper.
# Create a token at https://gitee.com/profile/personal_access_tokens (scope: projects).
#
# ASCII only on purpose: PowerShell 5.1 reads a BOM-less file as ANSI and Chinese
# comments then break lines (that is how the 1.13.0 release manifest lost its asset name).
$ErrorActionPreference = 'Stop'
$api = 'https://gitee.com/api/v5'
# Windows PowerShell 5.1 does not load this assembly on its own, and the attachment
# upload needs it.
Add-Type -AssemblyName System.Net.Http

$token = if ($env:GITEE_TOKEN) { $env:GITEE_TOKEN.Trim() } elseif (Test-Path -LiteralPath $TokenFile) { ([IO.File]::ReadAllText($TokenFile)).Trim() } else { '' }
if (-not $token) { throw "No Gitee token. Create one at https://gitee.com/profile/personal_access_tokens (scope: projects) and save it on its own line in $TokenFile, or set GITEE_TOKEN." }

function Call-Gitee {
  param([string]$Method, [string]$Path, [hashtable]$Fields, [string]$UploadFile, [switch]$Json)
  $uri = $api + $Path
  $separator = if ($uri.Contains('?')) { '&' } else { '?' }
  $authorized = $uri + $separator + 'access_token=' + $token
  try {
    if ($UploadFile) {
      $client = New-Object System.Net.Http.HttpClient
      $client.Timeout = [TimeSpan]::FromMinutes(60)
      $form = New-Object System.Net.Http.MultipartFormDataContent
      $bytes = [IO.File]::ReadAllBytes($UploadFile)
      # PowerShell unrolls a byte array into separate arguments, so wrap it in a
      # one-element array to reach the ByteArrayContent(byte[]) constructor.
      $part = New-Object System.Net.Http.ByteArrayContent -ArgumentList (, $bytes)
      $part.Headers.ContentType = New-Object System.Net.Http.Headers.MediaTypeHeaderValue('application/octet-stream')
      $form.Add($part, 'file', [IO.Path]::GetFileName($UploadFile))
      $response = $client.PostAsync($authorized, $form).Result
      $body = $response.Content.ReadAsStringAsync().Result
      $client.Dispose()
      if (-not $response.IsSuccessStatusCode) { throw ("HTTP " + [int]$response.StatusCode + " " + $body) }
      return $body
    }
    # PATCH needs a JSON body: Gitee answers 406 for a form-encoded PATCH.
    if ($Json) {
      $text = $Fields | ConvertTo-Json
      return Invoke-RestMethod -Method $Method -Uri $authorized -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($text)) -TimeoutSec 300
    }
    if ($Fields) { return Invoke-RestMethod -Method $Method -Uri $authorized -Body $Fields -TimeoutSec 300 }
    return Invoke-RestMethod -Method $Method -Uri $authorized -TimeoutSec 120
  }
  catch {
    $status = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    $detail = $_.Exception.Message
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $detail = $_.ErrorDetails.Message }
    throw ("Gitee API {0} {1} failed: HTTP {2} {3}" -f $Method, $Path, $status, $detail)
  }
}

function Get-Release {
  param([string]$Tag)
  # Gitee answers an unknown tag with HTTP 200 and an empty body, so a missing release
  # arrives as $null rather than as a 404.
  try {
    $found = Call-Gitee -Method GET -Path ("/repos/{0}/{1}/releases/tags/{2}" -f $GiteeOwner, $GiteeRepo, $Tag)
    if ($null -eq $found -or -not $found.id) {
      $all = @(Call-Gitee -Method GET -Path ("/repos/{0}/{1}/releases?per_page=100" -f $GiteeOwner, $GiteeRepo))
      $found = $all | Where-Object { $_.tag_name -eq $Tag } | Select-Object -First 1
    }
    return $found
  }
  catch { if ($_.Exception.Message -match 'HTTP 404') { return $null } else { throw } }
}

function Remove-Release {
  param($Release, [string]$Tag)
  # Drop leftover attachments of a previous attempt (for example one uploaded under the
  # wrong file name) so the release only ever carries the intended files.
  if ($Release) {
    try {
      foreach ($asset in @(Call-Gitee -Method GET -Path ("/repos/{0}/{1}/releases/{2}/attach_files" -f $GiteeOwner, $GiteeRepo, $Release.id))) {
        if ($asset.name -notlike 'PaperFlow-*' -and $asset.name -ne 'build-info.json' -and $asset.name -ne 'update.json') {
          try { $null = Call-Gitee -Method DELETE -Path ("/repos/{0}/{1}/releases/{2}/attach_files/{3}" -f $GiteeOwner, $GiteeRepo, $Release.id, $asset.id) } catch { }
        }
      }
    } catch { }
  }
  if ($Release) { try { $null = Call-Gitee -Method DELETE -Path ("/repos/{0}/{1}/releases/{2}" -f $GiteeOwner, $GiteeRepo, $Release.id) } catch { Write-Output ("could not delete the old release: " + $_.Exception.Message) } }
  try { $null = Call-Gitee -Method DELETE -Path ("/repos/{0}/{1}/tags/{2}" -f $GiteeOwner, $GiteeRepo, $Tag) } catch { }
}

function Upload-Assets {
  param($Release, [string[]]$Files, [string]$Label)
  $existing = @()
  try { $existing = @(Call-Gitee -Method GET -Path ("/repos/{0}/{1}/releases/{2}/attach_files" -f $GiteeOwner, $GiteeRepo, $Release.id) | ForEach-Object { $_.name }) } catch { }
  foreach ($file in $Files) {
    if (-not (Test-Path -LiteralPath $file)) { Write-Output ("skip (missing): {0}" -f $file); continue }
    $name = [IO.Path]::GetFileName($file)
    if ($existing -contains $name) { Write-Output ("already on {0}: {1}" -f $Label, $name); continue }
    $null = Call-Gitee -Method POST -Path ("/repos/{0}/{1}/releases/{2}/attach_files" -f $GiteeOwner, $GiteeRepo, $Release.id) -UploadFile $file
    Write-Output ("uploaded to {0}: {1}" -f $Label, $name)
  }
}

# ---------- 1. who is this token ----------
$me = Call-Gitee -Method GET -Path '/user'
if (-not $GiteeOwner) { $GiteeOwner = $me.login }
Write-Output ("Token belongs to {0}; mirroring into https://gitee.com/{1}/{2}" -f $me.login, $GiteeOwner, $GiteeRepo)

if ($DryRun) {
  Write-Output 'DRY RUN - nothing else will be written to Gitee.'
  Write-Output ("push source : {0} (branches + tags)" -f $Source)
  if ($Version) { Write-Output ("release     : v{0} + a fixed latest release" -f $Version); Write-Output ("assets      : {0}" -f $ArtifactsDir) }
  return
}

# ---------- 2. repository ----------
$exists = $true
try { $null = Call-Gitee -Method GET -Path ("/repos/{0}/{1}" -f $GiteeOwner, $GiteeRepo) }
catch { if ($_.Exception.Message -match 'HTTP 404') { $exists = $false } else { throw } }
if (-not $exists) {
  $fields = @{
    name = $GiteeRepo
    description = 'Windows desktop widget that tracks paper submission progress. Mirror of https://github.com/pwya/paperflow'
    private = 'false'; auto_init = 'false'; has_issues = 'true'
  }
  $created = if ($GiteeOwner -eq $me.login) { Call-Gitee -Method POST -Path '/user/repos' -Fields $fields } else { Call-Gitee -Method POST -Path ("/orgs/{0}/repos" -f $GiteeOwner) -Fields $fields }
  Write-Output ("Created repository: {0}" -f $created.html_url)
} else { Write-Output ("Repository exists: https://gitee.com/{0}/{1}" -f $GiteeOwner, $GiteeRepo) }

# A mirror nobody can read is useless, and Gitee creates repositories as private by
# default. Flip it to public and say so if Gitee refuses.
$visibility = Call-Gitee -Method GET -Path ("/repos/{0}/{1}" -f $GiteeOwner, $GiteeRepo)
if ($visibility.private) {
  $made = Call-Gitee -Method PATCH -Json -Path ("/repos/{0}/{1}" -f $GiteeOwner, $GiteeRepo) -Fields @{
    name = $GiteeRepo; private = $false; has_issues = $true
    description = 'Windows desktop widget that tracks paper submission progress. Mirror of https://github.com/pwya/paperflow'
  }
  Write-Output ('Repository visibility set to: ' + $(if ($made.private) { 'private (Gitee refused - real-name verification may be required)' } else { 'public' }))
}

# ---------- 3. push branches and tags ----------
$askpass = Join-Path ([IO.Path]::GetTempPath()) ('gitee-askpass-' + [guid]::NewGuid().ToString('N') + '.cmd')
[IO.File]::WriteAllText($askpass, "@echo off`r`necho %GITEE_TOKEN%`r`n", [Text.Encoding]::ASCII)
$env:GITEE_TOKEN = $token
$env:GIT_ASKPASS = $askpass
$env:GIT_TERMINAL_PROMPT = '0'
try {
  $pushUrl = "https://$GiteeOwner@gitee.com/$GiteeOwner/$GiteeRepo.git"
  # Release tags are created on GitHub by gh release create and are not in the local
  # clone until they are fetched. Prune branches only: pruning tags once deleted the
  # tag a Gitee release pointed at.
  & git -C $Source fetch --tags --quiet origin
  if ($LASTEXITCODE -ne 0) { Write-Output 'warning: could not fetch tags from origin; tags may be missing on the mirror' }
  & git -C $Source -c credential.helper= push --prune $pushUrl 'refs/heads/*:refs/heads/*'
  if ($LASTEXITCODE -ne 0) { throw "git push (branches) to Gitee failed with exit code $LASTEXITCODE" }
  # "+" because Gitee creates the tag itself when a release is made, and a plain push
  # then refuses to touch it ("already exists").
  & git -C $Source -c credential.helper= push $pushUrl '+refs/tags/*:refs/tags/*'
  if ($LASTEXITCODE -ne 0) { throw "git push (tags) to Gitee failed with exit code $LASTEXITCODE" }
  Write-Output 'Pushed branches and tags.'
} finally {
  if (Test-Path -LiteralPath $askpass) { [IO.File]::Delete($askpass) }
  Remove-Item Env:GIT_ASKPASS -ErrorAction SilentlyContinue
}

# ---------- 4. releases ----------
if ($Version) {
  $tag = 'v' + $Version
  $localManifest = Join-Path $ArtifactsDir 'update.json'
  if (-not (Test-Path -LiteralPath $localManifest)) { throw "missing $localManifest" }
  $manifest = Get-Content -LiteralPath $localManifest -Raw -Encoding UTF8 | ConvertFrom-Json

  # A Gitee-flavoured manifest: same version, same checksums, but the download points at
  # the Gitee copy so a user in China does not fall back to the slow GitHub download.
  # The download is the zip; exeSha256/exeLength describe the program inside it.
  $assetName = "PaperFlow-$Version-win-x64.zip"
  $giteeUrl = "https://gitee.com/$GiteeOwner/$GiteeRepo/releases/download/$tag/$assetName"
  # The file name matters: the attachment has to be called update.json so the app's
  # fixed URL .../releases/download/latest/update.json resolves.
  $manifestFolder = Join-Path ([IO.Path]::GetTempPath()) ("gitee-manifest-" + [guid]::NewGuid().ToString('N'))
  [IO.Directory]::CreateDirectory($manifestFolder) | Out-Null
  $giteeManifest = Join-Path $manifestFolder 'update.json'
  # 从本地清单整份抄过来，只改下载地址。挨个字段重写会让后加的字段（比如 notes，也就是
  # 程序里"这次改了什么"显示的原文）在镜像上凭空消失。
  $giteeFields = [ordered]@{}
  foreach ($property in $manifest.PSObject.Properties) { $giteeFields[$property.Name] = $property.Value }
  $giteeFields['url'] = $giteeUrl
  [IO.File]::WriteAllText($giteeManifest, ($giteeFields | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

  $release = Get-Release -Tag $tag
  if (-not $release) {
    $body = if ($NotesFile -and (Test-Path -LiteralPath $NotesFile)) { [IO.File]::ReadAllText($NotesFile) } else { "PaperFlow $Version" }
    $release = Call-Gitee -Method POST -Path ("/repos/{0}/{1}/releases" -f $GiteeOwner, $GiteeRepo) -Fields @{ tag_name = $tag; name = "PaperFlow $Version"; body = $body; target_commitish = 'main'; prerelease = 'false' }
    Write-Output ("Created release {0}" -f $tag)
  } else { Write-Output ("Release {0} already exists" -f $tag) }
  Upload-Assets -Release $release -Label $tag -Files @(
    (Join-Path $ArtifactsDir $assetName),
    (Join-Path $ArtifactsDir "PaperFlow-$Version-source.zip"),
    (Join-Path $ArtifactsDir 'build-info.json'),
    $giteeManifest
  )

  # The fixed pointer the app reads: always recreated so its update.json is the newest.
  Remove-Release -Release (Get-Release -Tag 'latest') -Tag 'latest'
  Start-Sleep -Seconds 2
  $latest = Call-Gitee -Method POST -Path ("/repos/{0}/{1}/releases" -f $GiteeOwner, $GiteeRepo) -Fields @{
    tag_name = 'latest'; name = 'latest (for the in-app updater)'; target_commitish = 'main'; prerelease = 'false'
    body = "This release only carries update.json, the manifest the app reads first. The program files live in the version release ($tag)."
  }
  Upload-Assets -Release $latest -Label 'latest' -Files @($giteeManifest)

  # ---------- 5. prove the updater's first URL really works ----------
  $manifestUrl = "https://gitee.com/$GiteeOwner/$GiteeRepo/releases/download/latest/update.json"
  $check = Invoke-RestMethod -Uri $manifestUrl -TimeoutSec 60
  if ($check.version -ne $manifest.version -or $check.sha256 -ne $manifest.sha256 -or [long]$check.length -ne [long]$manifest.length) { throw "the mirrored manifest does not match the local one" }
  # Windows PowerShell 5.1 will not let -Headers carry Range and throws on HEAD, so ask
  # .NET directly: one HEAD proves the 68 MB download is reachable without pulling it.
  $probeClient = New-Object System.Net.Http.HttpClient
  $probeRequest = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Head, $check.url)
  $probe = $probeClient.SendAsync($probeRequest).Result
  $probeClient.Dispose()
  Write-Output ("Verified {0} -> version {1}, {2} bytes, download HTTP {3}" -f $manifestUrl, $check.version, $check.length, [int]$probe.StatusCode)
  Write-Output ("Release page: https://gitee.com/{0}/{1}/releases/{2}" -f $GiteeOwner, $GiteeRepo, $tag)
}

Write-Output ("Done. Mirror: https://gitee.com/{0}/{1}" -f $GiteeOwner, $GiteeRepo)
