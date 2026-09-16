param([switch]$Staged)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $files = if ($Staged) { @(git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMR) } else { @(git -c core.quotepath=false ls-files) }
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect Git files.' }
    # Whitelist, not blacklist. docs/ is pinned to the one public document on purpose:
    # the author's planning notes, hand-off documents, roadmaps and personal runbooks live
    # in a synced private folder outside the repository and must never be copied in.
    $allowed = '^(README\.md|LICENSE|CHANGELOG\.md|CONTRIBUTING\.md|SECURITY\.md|AGENTS\.md|\.gitignore|\.gitattributes|PaperFlow/[^/]+\.(cs|xaml|csproj)|PaperFlow/Assets/app\.ico|Launcher/[^/]+\.cs|tests/[^/]+\.(cs|csproj)|scripts/[^/]+\.ps1|docs/RELEASING\.md|docs/images/[0-9a-z\-]+\.png|\.github/workflows/[^/]+\.ya?ml|\.githooks/pre-commit)$'
    $problems = [Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        if ($file -notmatch $allowed) { $problems.Add("Unapproved public path: $file"); continue }
        if ($file -match '(?i)(papers.*\.json|\.local\.|\.env|credential|secret|\.lnk$|\.log$|\.pdb$)') { $problems.Add("Private/runtime filename: $file"); continue }
        # Binary assets are checked by the whitelist above only: reading a PNG as text
        # would produce meaningless matches and slow every commit down.
        if ($file -like '*.ico' -or $file -like '*.png') { continue }
        $content = if ($Staged) { (git show ":$file") -join "`n" } else { Get-Content -LiteralPath $file -Raw -Encoding UTF8 }
        # Scan the exact staged content, not merely the working-tree version.
        # Cloud folder names stay in this deny pattern on purpose: it exists to catch a leaked
        # personal install path, not to advertise any one provider.
        if ($content -match '(?i)([A-Z]:[\\/](Users|OneDrive|Dropbox|Google|iCloud|Dev|stata_projects)[\\/]|notion\.site/[0-9a-f]{32}|-----BEGIN [A-Z ]*PRIVATE KEY-----|gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,})') { $problems.Add("Private path, page identifier or credential pattern: $file") }
        $localName = [Environment]::UserName
        if ($localName.Length -ge 4 -and $content.Contains($localName)) { $problems.Add("Local username appears in source: $file") }
    }
    if ($problems.Count) { throw ($problems -join "`n") }
    Write-Output "Public-source audit passed ($($files.Count) files). Review real content before publishing; this check is an additional safeguard."
} finally { Pop-Location }
