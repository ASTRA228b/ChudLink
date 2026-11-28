param(
    [Parameter(Mandatory)][string]$Commit,
    [string]$ReleaseDirectory = 'artifacts/release-0.1.1',
    [string]$PortableDirectory = 'artifacts/portable-drafts',
    [switch]$Publish
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$taskRoot = Split-Path $PSScriptRoot -Parent
Set-Location -LiteralPath $taskRoot
if ($Commit -notmatch '^[a-f0-9]{40}$') { throw 'Use a verified full commit SHA.' }
$taskRepository = 'https://api.github.com/repos/ASTRA228b/ChudLink'
$taskFiles = @(Get-ChildItem -LiteralPath $ReleaseDirectory -File)
if (-not ($taskFiles.Name -contains 'ChudLink-0.1.1-Alpha-Windows-x64.zip') -or -not ($taskFiles.Name -contains 'ChudLink-0.1.1-Alpha.apk')) { throw 'Both Windows and Android release files are required.' }
$taskPortable = @(Get-ChildItem -LiteralPath $PortableDirectory -File -Filter 'ChudLink-0.1.1-portable-draft-*.zip')
if ($taskPortable.Count -ne 4) { throw 'All four cross-published draft archives are required.' }
$taskPrompt = $env:GIT_TERMINAL_PROMPT; $taskInteractive = $env:GCM_INTERACTIVE
$taskCredential = $null; $taskToken = $null; $taskHeaders = $null
try {
    $env:GIT_TERMINAL_PROMPT = '0'; $env:GCM_INTERACTIVE = 'Never'
    $taskCredential = "protocol=https`nhost=github.com`n`n" | git credential fill
    if ($LASTEXITCODE -ne 0) { throw 'GitHub credentials are unavailable.' }
    foreach ($taskLine in $taskCredential) { if ($taskLine.StartsWith('password=')) { $taskToken = $taskLine.Substring(9) } }
    if (-not $taskToken) { throw 'GitHub credentials are unavailable.' }
    $taskHeaders = @{ Authorization = "Bearer $taskToken"; Accept = 'application/vnd.github+json'; 'User-Agent' = 'ChudLink-Release'; 'X-GitHub-Api-Version' = '2022-11-28' }
    $taskUser = Invoke-RestMethod -Uri 'https://api.github.com/user' -Headers $taskHeaders
    if ($taskUser.login -ne 'ASTRA228b') { throw 'The authenticated account is not the expected repository owner.' }
    function Prepare-Release([string]$Tag, [string]$Name, [string]$Body, [IO.FileInfo[]]$Files, [bool]$PublishNow) {
        $taskExisting = Invoke-RestMethod -Uri ($taskRepository + '/releases?per_page=100') -Headers $taskHeaders
        $taskRelease = $taskExisting | Where-Object tag_name -EQ $Tag | Select-Object -First 1
        if ($taskRelease -and -not $taskRelease.draft) { throw "Release $Tag is already public; refusing to overwrite it." }
        $taskBody = @{ tag_name = $Tag; target_commitish = $Commit; name = $Name; body = $Body; draft = $true; prerelease = $true } | ConvertTo-Json
        if ($taskRelease) { $taskRelease = Invoke-RestMethod -Method Patch -Uri ($taskRepository + '/releases/' + $taskRelease.id) -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body $taskBody }
        else { $taskRelease = Invoke-RestMethod -Method Post -Uri ($taskRepository + '/releases') -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body $taskBody }
        foreach ($taskFile in $Files) {
            $taskHash = (Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            $taskAssets = Invoke-RestMethod -Uri $taskRelease.assets_url -Headers $taskHeaders
            $taskAsset = $taskAssets | Where-Object name -EQ $taskFile.Name | Select-Object -First 1
            if ($taskAsset) {
                if ($taskAsset.digest -ne ('sha256:' + $taskHash) -or $taskAsset.size -ne $taskFile.Length) { throw "Existing draft asset differs: $($taskFile.Name). Inspect it before retrying." }
            } else {
                $taskUpload = ($taskRelease.upload_url -replace '\{.*$', '') + '?name=' + [Uri]::EscapeDataString($taskFile.Name)
                $taskAsset = Invoke-RestMethod -Method Post -Uri $taskUpload -Headers $taskHeaders -ContentType 'application/octet-stream' -InFile $taskFile.FullName -TimeoutSec 600
                if ($taskAsset.digest -ne ('sha256:' + $taskHash) -or $taskAsset.size -ne $taskFile.Length -or $taskAsset.state -ne 'uploaded') { throw "Uploaded asset verification failed: $($taskFile.Name)" }
            }
            Write-Host "Verified $($taskFile.Name) ($($taskFile.Length) bytes)"
        }
        if ($PublishNow) {
            $taskRelease = Invoke-RestMethod -Method Patch -Uri ($taskRepository + '/releases/' + $taskRelease.id) -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body '{"draft":false,"prerelease":true,"make_latest":"false"}'
        }
        [pscustomobject]@{ tag = $Tag; url = $taskRelease.html_url; draft = $taskRelease.draft; id = $taskRelease.id }
    }
    # Upload and verify every public-build asset before making the Windows/Android prerelease public.
    $taskPublic = Prepare-Release 'v0.1.1-alpha' 'ChudLink 0.1.1 Alpha - performance and easier phone setup' ([IO.File]::ReadAllText((Join-Path $taskRoot 'docs/RELEASE-0.1.1.md'))) $taskFiles $false
    $taskDraft = Prepare-Release 'v0.1.1-portable-draft' 'ChudLink Linux & macOS - development drafts' ([IO.File]::ReadAllText((Join-Path $taskRoot 'docs/PORTABLE-DRAFTS.md'))) $taskPortable $false
    if ($Publish) {
        $taskPublished = Invoke-RestMethod -Method Patch -Uri ($taskRepository + '/releases/' + $taskPublic.id) -Headers $taskHeaders -ContentType 'application/json; charset=utf-8' -Body '{"draft":false,"prerelease":true,"make_latest":"false"}'
        $taskPublic = [pscustomobject]@{tag=$taskPublished.tag_name;url=$taskPublished.html_url;draft=$taskPublished.draft;id=$taskPublished.id}
    }
    @($taskPublic, $taskDraft) | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskRoot 'artifacts/github-release-result.json') -Encoding utf8
    @($taskPublic, $taskDraft) | ConvertTo-Json -Compress
} finally {
    $taskCredential = $null; $taskToken = $null; $taskHeaders = $null
    $env:GIT_TERMINAL_PROMPT = $taskPrompt; $env:GCM_INTERACTIVE = $taskInteractive
}
