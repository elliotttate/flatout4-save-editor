# Build a single setup exe for the WinUI app.
#
# WinUI 3 apps can crash when published directly with PublishSingleFile because
# Microsoft.UI.Xaml expects its native/runtime files beside the app exe. This
# script publishes the app as a normal self-contained multi-file folder, zips
# that folder, embeds the zip in a small non-WinUI single-file launcher, and
# writes one setup exe to artifacts\release.

param(
    [string]$Version = "0.3.0",
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"

$versionText = $Version.TrimStart("v", "V")
$numericVersion = ($versionText -replace "[^0-9.].*$", "")
if ([string]::IsNullOrWhiteSpace($numericVersion)) {
    throw "Version '$Version' does not start with a numeric version."
}

$parts = @($numericVersion.Split(".") | Select-Object -First 4)
while ($parts.Count -lt 4) {
    $parts += "0"
}
$assemblyVersion = $parts -join "."

$appProject = Join-Path $RepoRoot "FlatOut4SaveEditor.csproj"
$publishDirCandidates = @(
    (Join-Path $RepoRoot "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"),
    (Join-Path $RepoRoot "bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish")
)

Write-Host "Publishing FlatOut4SaveEditor (self-contained multi-file)..."
foreach ($candidate in $publishDirCandidates) {
    if (Test-Path $candidate) {
        Remove-Item -Recurse -Force $candidate
    }
}

& dotnet publish $appProject -c Release -p:Platform=x64 -r win-x64 --self-contained true -v:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed: $LASTEXITCODE"
}

$publishDir = $publishDirCandidates |
    Where-Object { Test-Path (Join-Path $_ "FlatOut4SaveEditor.exe") } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($publishDir)) {
    throw "Publish did not produce FlatOut4SaveEditor.exe in any expected publish folder."
}

$resourceDir = Join-Path $RepoRoot "Launcher\Resources"
New-Item -ItemType Directory -Path $resourceDir -Force | Out-Null
$zipPath = Join-Path $resourceDir "FlatOut4SaveEditor.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Zipping publish folder into launcher resource..."
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -CompressionLevel Optimal
$zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  -> $zipPath ($zipMB MB)"

$launcherProject = Join-Path $RepoRoot "Launcher\Launcher.csproj"
$launcherPublishDir = Join-Path $RepoRoot "Launcher\bin\Release\net8.0-windows\win-x64\publish"
if (Test-Path $launcherPublishDir) {
    Remove-Item -Recurse -Force $launcherPublishDir
}

Write-Host "Publishing setup launcher (single-file)..."
& dotnet publish $launcherProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Version=$versionText `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$versionText `
    -v:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "launcher dotnet publish failed: $LASTEXITCODE"
}

$launcherExe = Join-Path $launcherPublishDir "FlatOut4SaveEditorSetup.exe"
if (-not (Test-Path $launcherExe)) {
    throw "Launcher publish produced no exe at $launcherExe"
}

$artifactDir = Join-Path $RepoRoot "artifacts\release"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
$finalExe = Join-Path $artifactDir ("FlatOut4SaveEditor-v{0}-Setup.exe" -f $versionText)
if (Test-Path $finalExe) {
    Remove-Item $finalExe -Force
}

Copy-Item $launcherExe $finalExe
$finalMB = [math]::Round((Get-Item $finalExe).Length / 1MB, 1)

Write-Host ""
Write-Host "Built $finalExe ($finalMB MB)"
