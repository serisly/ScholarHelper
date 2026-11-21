param(
    [Parameter(Mandatory = $false)]
    [string]$GitHubOwner = "serisly",

    [Parameter(Mandatory = $false)]
    [string]$GitHubRepo = "ScholarHelper"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "ScholarHelper.csproj"
$releaseBundleDir = Join-Path $repoRoot "bin\x64\Release\ScholarHelper"
$distDir = Join-Path $repoRoot "dist"

Write-Host "Building ScholarHelper (x64 Release)..." -ForegroundColor Cyan
dotnet build $csproj -c Release -p:Platform=x64 --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$zipSrc = Join-Path $releaseBundleDir "latest.zip"
$manifestSrc = Join-Path $releaseBundleDir "ScholarHelper.json"
if (-not (Test-Path $zipSrc) -or -not (Test-Path $manifestSrc)) {
    throw "Expected SDK-packaged files not found in $releaseBundleDir"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null
$zipDst = Join-Path $distDir "latest.zip"
Copy-Item -Force $zipSrc $zipDst

$iconSrc = Join-Path $repoRoot "images\icon.png"
if (Test-Path $iconSrc) {
    Copy-Item -Force $iconSrc (Join-Path $distDir "icon.png")
}

$manifest = Get-Content -Raw -Path $manifestSrc | ConvertFrom-Json
$downloadUrl = "https://raw.githubusercontent.com/$GitHubOwner/$GitHubRepo/main/dist/latest.zip"
$iconUrl = "https://raw.githubusercontent.com/$GitHubOwner/$GitHubRepo/main/images/icon.png"

$repoEntry = [ordered]@{
    Author              = $manifest.Author
    Name                = $manifest.Name
    InternalName        = $manifest.InternalName
    AssemblyVersion     = $manifest.AssemblyVersion
    Description         = $manifest.Description
    ApplicableVersion   = $manifest.ApplicableVersion
    RepoUrl             = "https://github.com/$GitHubOwner/$GitHubRepo"
    IconUrl             = $iconUrl
    DalamudApiLevel     = [int]$manifest.DalamudApiLevel
    LoadRequiredState   = [int]$manifest.LoadRequiredState
    LoadSync            = [bool]$manifest.LoadSync
    CanUnloadAsync      = [bool]$manifest.CanUnloadAsync
    LoadPriority        = [int]$manifest.LoadPriority
    Punchline           = $manifest.Punchline
    AcceptsFeedback     = [bool]$manifest.AcceptsFeedback
    IsHide              = $false
    IsTestingExclusive  = $false
    LastUpdate          = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    DownloadLinkInstall = $downloadUrl
    DownloadLinkUpdate  = $downloadUrl
    DownloadLinkTesting = $downloadUrl
    Tags                = @("scholar", "healer", "hotbar", "cooldown", "utility")
}

$repoJsonPath = Join-Path $repoRoot "repo.json"
$entryJson = $repoEntry | ConvertTo-Json -Depth 4
$repoJson = "[$entryJson]"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($repoJsonPath, $repoJson, $utf8NoBom)
Copy-Item -Force $repoJsonPath (Join-Path $distDir "repo.json")

Write-Host "Packaged cleanly:" -ForegroundColor Green
Write-Host "  ZIP      : $zipDst ($((Get-Item $zipDst).Length) bytes)"
Write-Host "  REPO     : $repoJsonPath"
Write-Host "  ICON     : $iconUrl"
Write-Host "  INSTALL  : https://raw.githubusercontent.com/$GitHubOwner/$GitHubRepo/main/repo.json"
