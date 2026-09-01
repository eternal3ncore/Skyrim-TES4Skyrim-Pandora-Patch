param(
    [string]$SkyrimRoot = "C:\Games\Skyrim",
    [string]$PandoraReferenceDir = "",
    [string]$PandoraVersion = "v4.4.0-beta",
    [switch]$NoReferenceDownload
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$ProjectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectFile = Join-Path $ProjectRoot "TES4ConverterPandoraPatch.csproj"
$VersionName = "TES4ConverterPandoraPatch_v0_3"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK was not found. Pandora v4.4.0-beta and its example native plugin target net10.0; install/enable the .NET 10 SDK and rerun this script."
}

function Find-PandoraApiInDirectory {
    param([string]$Root)
    if ([string]::IsNullOrWhiteSpace($Root) -or -not (Test-Path -LiteralPath $Root)) { return $null }
    foreach ($name in @("Pandora API.dll", "Pandora.API.dll")) {
        $direct = Join-Path $Root $name
        if (Test-Path -LiteralPath $direct) { return Get-Item -LiteralPath $direct }
    }
    foreach ($name in @("Pandora API.dll", "Pandora.API.dll")) {
        $found = Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $name -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($found) { return $found }
    }
    return $null
}

$api = $null
if (-not [string]::IsNullOrWhiteSpace($PandoraReferenceDir)) {
    $api = Find-PandoraApiInDirectory $PandoraReferenceDir
    if (-not $api) { throw "Pandora API assembly not found in -PandoraReferenceDir: $PandoraReferenceDir" }
}
else {
    $searchRoots = New-Object System.Collections.Generic.List[string]
    if (Test-Path -LiteralPath $SkyrimRoot) { $searchRoots.Add($SkyrimRoot) }
    try {
        $gamesRoot = Split-Path -Parent $SkyrimRoot
        if ($gamesRoot -and (Test-Path -LiteralPath $gamesRoot)) { $searchRoots.Add($gamesRoot) }
    } catch { }
    if ($env:USERPROFILE) {
        $downloads = Join-Path $env:USERPROFILE "Downloads"
        if (Test-Path -LiteralPath $downloads) { $searchRoots.Add($downloads) }
    }
    foreach ($root in ($searchRoots | Select-Object -Unique)) {
        Write-Host "Searching for Pandora API.dll beneath: $root"
        $api = Find-PandoraApiInDirectory $root
        if ($api) { break }
    }
    if (-not $api -and -not $NoReferenceDownload) {
        $cacheRoot = Join-Path $ProjectRoot ".pandora-reference\$PandoraVersion"
        $cachedApi = Find-PandoraApiInDirectory $cacheRoot
        if ($cachedApi) { $api = $cachedApi }
        else {
            New-Item -ItemType Directory -Path $cacheRoot -Force | Out-Null
            $zipName = "Pandora_Behaviour_Engine_$PandoraVersion.zip"
            $zipPath = Join-Path $cacheRoot $zipName
            $releaseUrl = "https://github.com/Monitor221hz/Pandora-Behaviour-Engine-Plus/releases/download/$PandoraVersion/$zipName"
            Write-Host "No loose Pandora API.dll was found locally."
            Write-Host "Downloading Pandora $PandoraVersion loose-build reference package..."
            Write-Host $releaseUrl
            try { Invoke-WebRequest -Uri $releaseUrl -OutFile $zipPath -UseBasicParsing }
            catch { throw "Could not download Pandora reference package. Either restore internet access and rerun, or pass -PandoraReferenceDir pointing to a loose Pandora build containing 'Pandora API.dll'. Download error: $($_.Exception.Message)" }
            $extractRoot = Join-Path $cacheRoot "extracted"
            if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
            New-Item -ItemType Directory -Path $extractRoot -Force | Out-Null
            Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
            $api = Find-PandoraApiInDirectory $extractRoot
            if (-not $api) { throw "Downloaded Pandora $PandoraVersion reference package, but no Pandora API.dll was present." }
        }
    }
}

if (-not $api) {
    throw "Could not obtain Pandora API.dll. Rerun with -PandoraReferenceDir pointing to a loose Pandora build, or omit -NoReferenceDownload so build.ps1 can fetch the pinned $PandoraVersion reference package automatically."
}

$PandoraApiPath = $api.FullName
$PandoraReferenceDir = $api.Directory.FullName
Write-Host "Pandora references: $PandoraReferenceDir"
Write-Host "Pandora API: $PandoraApiPath"
Write-Host "Building $VersionName..."

dotnet build $ProjectFile -c Release -p:PandoraApiPath="$PandoraApiPath"
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$BuildDir = Join-Path $ProjectRoot "bin\Release\net10.0"
$Dll = Join-Path $BuildDir "TES4ConverterCompatibility.dll"
if (-not (Test-Path -LiteralPath $Dll)) { throw "Built plugin DLL not found: $Dll" }

$StageRoot = Join-Path $ProjectRoot "dist\stage"
$ModRoot = Join-Path $StageRoot "Pandora_Engine\mod\TES4ConverterCompatibility"
$NativeRoot = Join-Path $ModRoot "native\TES4ConverterCompatibility"
if (Test-Path -LiteralPath $StageRoot) { Remove-Item -LiteralPath $StageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $NativeRoot -Force | Out-Null
Copy-Item (Join-Path $ProjectRoot "info.xml") (Join-Path $ModRoot "info.xml") -Force
Copy-Item $Dll (Join-Path $NativeRoot "TES4ConverterCompatibility.dll") -Force
$Deps = Join-Path $BuildDir "TES4ConverterCompatibility.deps.json"
if (Test-Path -LiteralPath $Deps) { Copy-Item $Deps (Join-Path $NativeRoot "TES4ConverterCompatibility.deps.json") -Force }
Copy-Item (Join-Path $ProjectRoot "payload\animationdatasinglefile.txt") (Join-Path $NativeRoot "animationdatasinglefile.txt") -Force
Copy-Item (Join-Path $ProjectRoot "payload\animationsetdatasinglefile.txt") (Join-Path $NativeRoot "animationsetdatasinglefile.txt") -Force
Copy-Item (Join-Path $ProjectRoot "PATCH_README.txt") (Join-Path $ModRoot "README.txt") -Force

$DistDir = Join-Path $ProjectRoot "dist"
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
$ZipPath = Join-Path $DistDir "$VersionName.zip"
if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $StageRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal
Write-Host ""
Write-Host "Built installable patch:"
Write-Host $ZipPath
Write-Host "Install the ZIP as a Skyrim mod, refresh Pandora, tick 'TES4Converter Compatibility', and Launch."
