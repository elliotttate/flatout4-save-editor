# Build both release downloads into artifacts\release:
#
#   FlatOut4SaveEditor-v<version>-Windows-x64.zip   portable, nothing installed. Its top level is just
#       FlatOut4SaveEditor.exe (a small native launcher, PortableLauncher\), README.md and App\, which
#       holds the WinUI app and its runtime. The launcher starts App\FlatOut4SaveEditor.exe.
#   FlatOut4SaveEditor-v<version>-Setup.exe          optional per-user install (Launcher\): extracts to
#       %LOCALAPPDATA%\FlatOut4SaveEditor\app, adds a Start menu shortcut and starts the app.
#
# WinUI 3 apps crash when published with PublishSingleFile (Microsoft.UI.Xaml wants its files beside
# the exe), so the app is always published as a normal self-contained folder and wrapped instead.
# Needs the .NET 8 SDK and Visual Studio's C++ tools (for the native launcher).
#
#   powershell -File scripts\build_release.ps1 -Version 0.6.0 [-RepoRoot <clean worktree>]

param(
    [string]$Version = "0.6.0",
    [string]$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

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

$artifactDir = Join-Path $RepoRoot "artifacts\release"
New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

# --- 1. The app, self-contained multi-file -------------------------------------------------------

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

# The app carries the release version too, so More > About can show it.
& dotnet publish $appProject `
    -c Release `
    -p:Platform=x64 `
    -r win-x64 `
    --self-contained true `
    -p:Version=$versionText `
    -p:AssemblyVersion=$assemblyVersion `
    -p:FileVersion=$assemblyVersion `
    -p:InformationalVersion=$versionText `
    -v:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed: $LASTEXITCODE"
}

$publishDir = $publishDirCandidates |
    Where-Object { Test-Path (Join-Path $_ "FlatOut4SaveEditor.exe") } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($publishDir)) {
    throw "Publish did not produce FlatOut4SaveEditor.exe in any expected publish folder."
}

# --- 2. Portable ZIP ------------------------------------------------------------------------------

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find "MSBuild\**\Bin\MSBuild.exe" |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($msbuild)) {
    throw "Visual Studio with the C++ desktop tools is needed to build the portable launcher."
}

Write-Host "Building the portable launcher (native)..."
& $msbuild (Join-Path $RepoRoot "PortableLauncher\PortableLauncher.vcxproj") `
    -t:Rebuild `
    -p:Configuration=Release `
    -p:Platform=x64 `
    -p:LauncherVersion=$versionText `
    -p:LauncherFileVersion=$assemblyVersion `
    -nologo -v:minimal | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "portable launcher build failed: $LASTEXITCODE"
}

$portableLauncher = Join-Path $RepoRoot "artifacts\portable-launcher\FlatOut4SaveEditor.exe"
if (-not (Test-Path $portableLauncher)) {
    throw "The launcher build produced no exe at $portableLauncher"
}

$stage = Join-Path $artifactDir "portable-stage"
if (Test-Path $stage) {
    Remove-Item -Recurse -Force $stage
}

New-Item -ItemType Directory -Path (Join-Path $stage "App") -Force | Out-Null
Copy-Item $portableLauncher (Join-Path $stage "FlatOut4SaveEditor.exe")
Copy-Item (Join-Path $RepoRoot "README.md") (Join-Path $stage "README.md")
Copy-Item -Path (Join-Path $publishDir "*") -Destination (Join-Path $stage "App") -Recurse

$portableZip = Join-Path $artifactDir ("FlatOut4SaveEditor-v{0}-Windows-x64.zip" -f $versionText)
if (Test-Path $portableZip) {
    Remove-Item $portableZip -Force
}

Write-Host "Zipping the portable layout..."
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $portableZip, [IO.Compression.CompressionLevel]::Optimal, $false)

# Extract it again and compare every file, so a release never ships a ZIP that differs from what
# was staged (or has anything besides the launcher, README and App folder at its top level).
Write-Host "Checking the portable ZIP..."
$verify = Join-Path ([IO.Path]::GetTempPath()) ("fo4se-zip-check-" + [Guid]::NewGuid().ToString("N"))
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($portableZip, $verify)
    $topLevel = @(Get-ChildItem $verify | ForEach-Object { $_.Name } | Sort-Object)
    if (($topLevel -join "|") -ne "App|FlatOut4SaveEditor.exe|README.md") {
        throw "Unexpected top level in the portable ZIP: $($topLevel -join ', ')"
    }

    $staged = @(Get-ChildItem $stage -Recurse -File)
    $extracted = @(Get-ChildItem $verify -Recurse -File)
    if ($staged.Count -ne $extracted.Count) {
        throw "The portable ZIP holds $($extracted.Count) files but $($staged.Count) were staged."
    }

    foreach ($file in $staged) {
        $relative = $file.FullName.Substring($stage.Length + 1)
        $copy = Join-Path $verify $relative
        if (-not (Test-Path -LiteralPath $copy) -or
            (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw "The portable ZIP's copy of $relative differs from the staged file."
        }
    }

    Write-Host ("  {0} files match; top level: {1}" -f $staged.Count, ($topLevel -join ", "))
}
finally {
    if (Test-Path $verify) {
        Remove-Item -Recurse -Force $verify
    }
}

# --- 3. Setup.exe (optional install) --------------------------------------------------------------

$resourceDir = Join-Path $RepoRoot "Launcher\Resources"
New-Item -ItemType Directory -Path $resourceDir -Force | Out-Null
$zipPath = Join-Path $resourceDir "FlatOut4SaveEditor.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Write-Host "Zipping publish folder into the setup resource..."
[IO.Compression.ZipFile]::CreateFromDirectory($publishDir, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $false)
$zipMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  -> $zipPath ($zipMB MB)"

$launcherProject = Join-Path $RepoRoot "Launcher\Launcher.csproj"
$launcherPublishDir = Join-Path $RepoRoot "Launcher\bin\Release\net8.0-windows\win-x64\publish"
if (Test-Path $launcherPublishDir) {
    Remove-Item -Recurse -Force $launcherPublishDir
}

Write-Host "Publishing the setup (single-file)..."
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
    throw "setup dotnet publish failed: $LASTEXITCODE"
}

$setupBuild = Join-Path $launcherPublishDir "FlatOut4SaveEditorSetup.exe"
if (-not (Test-Path $setupBuild)) {
    throw "The setup publish produced no exe at $setupBuild"
}

$setupExe = Join-Path $artifactDir ("FlatOut4SaveEditor-v{0}-Setup.exe" -f $versionText)
if (Test-Path $setupExe) {
    Remove-Item $setupExe -Force
}

Copy-Item $setupBuild $setupExe

# --- 4. Checksums ---------------------------------------------------------------------------------

$sums = foreach ($asset in @($portableZip, $setupExe)) {
    "{0}  {1}" -f (Get-FileHash -LiteralPath $asset -Algorithm SHA256).Hash, (Split-Path $asset -Leaf)
}
$sumsPath = Join-Path $artifactDir "SHA256SUMS.txt"
[IO.File]::WriteAllLines($sumsPath, [string[]]$sums)

Write-Host ""
foreach ($asset in @($portableZip, $setupExe, $sumsPath)) {
    Write-Host ("Built {0} ({1} MB)" -f $asset, [math]::Round((Get-Item $asset).Length / 1MB, 1))
}
