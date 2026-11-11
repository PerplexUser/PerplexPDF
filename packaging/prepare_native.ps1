#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$Cache,
    [string]$CrtDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$lock = Get-Content -Raw (Join-Path $PSScriptRoot 'native-lock.json') | ConvertFrom-Json
$Destination = [IO.Path]::GetFullPath($Destination)
$Cache = [IO.Path]::GetFullPath($Cache)
New-Item -ItemType Directory -Force $Destination, $Cache | Out-Null
$notices = Join-Path $Destination 'licenses'
New-Item -ItemType Directory -Force $notices | Out-Null
$headers = @{ 'User-Agent' = 'PerplexPDF-build'; Accept = 'application/vnd.github+json' }
if ($env:GH_TOKEN) { $headers.Authorization = "Bearer $env:GH_TOKEN" }
$provenance = [ordered]@{}

function Get-Api([string]$Path) {
    Invoke-RestMethod -Uri "https://api.github.com/$Path" -Headers $headers -TimeoutSec 90
}

function Get-ReleaseAsset($Entry, [string]$FixedHash) {
    $tag = [uri]::EscapeDataString($Entry.tag)
    $release = Get-Api "repos/$($Entry.repository)/releases/tags/$tag"
    $asset = @($release.assets | Where-Object name -CEQ $Entry.asset)
    if ($asset.Count -ne 1) { throw "Release asset missing: $($Entry.asset)" }
    $digest = $asset[0].digest
    $expected = $FixedHash
    if (!$expected) {
        if (!$release.immutable -or $digest -notmatch '^sha256:[0-9a-f]{64}$') {
            throw "The pinned asset needs an immutable release and a published SHA-256: $($Entry.asset)"
        }
        $expected = $digest.Substring(7)
    } elseif ($digest -and $digest -ne "sha256:$expected") {
        throw "Published digest differs from the locked digest: $($Entry.asset)"
    }

    $path = Join-Path $Cache $Entry.asset
    if (!(Test-Path -LiteralPath $path)) {
        $partial = "$path.partial"
        Invoke-WebRequest -Uri $asset[0].browser_download_url -OutFile $partial -TimeoutSec 180
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $partial).Hash -ine $expected) {
            Remove-Item -LiteralPath $partial
            throw "SHA-256 mismatch: $($Entry.asset)"
        }
        Move-Item -LiteralPath $partial -Destination $path
    }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash -ine $expected) {
        throw "Cached file has an unexpected SHA-256: $($Entry.asset)"
    }
    $provenance[$Entry.asset] = @{ tag = $Entry.tag; url = $asset[0].browser_download_url; sha256 = $expected }
    return $path
}

function Get-Commit([string]$Repository, [string]$Tag, [string]$ExpectedPrefix) {
    $ref = [uri]::EscapeDataString($Tag)
    $commit = (Get-Api "repos/$Repository/commits/$ref").sha
    if ($commit -notmatch '^[0-9a-f]{40}$' -or ($ExpectedPrefix -and !$commit.StartsWith($ExpectedPrefix))) {
        throw "Git tag no longer matches its locked commit: $Repository $Tag"
    }
    return $commit
}

function Get-RepositoryFile([string]$Repository, [string]$Commit, [string]$Name, [string]$Target) {
    $file = Get-Api "repos/$Repository/contents/${Name}?ref=$Commit"
    if ($file.type -ne 'file' -or $file.size -gt 32MB -or $file.size -lt 1) {
        throw "Unexpected source object: $Repository $Name"
    }
    Invoke-WebRequest -Uri $file.download_url -OutFile $Target -TimeoutSec 180
    $bytes = [IO.File]::ReadAllBytes($Target)
    $hash = [Security.Cryptography.IncrementalHash]::CreateHash([Security.Cryptography.HashAlgorithmName]::SHA1)
    try {
        $hash.AppendData([Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)" + [char]0))
        $hash.AppendData($bytes)
        $actual = [Convert]::ToHexString($hash.GetHashAndReset())
    } finally { $hash.Dispose() }
    if ($actual -ine $file.sha) { throw "Source blob mismatch: $Name" }
    $provenance["$Repository/$Name"] = @{
        commit = $Commit; blob = $file.sha; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $Target).Hash.ToLowerInvariant()
    }
}

function Copy-Notices([string]$Source, [string]$Name) {
    $target = Join-Path $notices $Name
    New-Item -ItemType Directory -Force $target | Out-Null
    $documents = @(Get-ChildItem -LiteralPath $Source -Recurse -File |
        Where-Object { $_.Name -match '(?i)license|notice|copyright|copying' -and $_.Extension -notin '.exe','.dll','.lib','.pdb' })
    if (!$documents.Count) { throw "No dependency notices found in $Source" }
    foreach ($document in $documents) {
        $path = Join-Path $target ([IO.Path]::GetRelativePath($Source, $document.FullName))
        New-Item -ItemType Directory -Force (Split-Path $path -Parent) | Out-Null
        Copy-Item -LiteralPath $document.FullName -Destination $path -Force
    }
}

$pdfiumCommit = Get-Commit $lock.pdfium.repository $lock.pdfium.tag $lock.pdfium.releaseCommitPrefix
$pdfiumArchive = Get-ReleaseAsset $lock.pdfium ''
$pdfiumExtract = Join-Path $Cache "pdfium-$($lock.pdfium.version)"
New-Item -ItemType Directory -Force $pdfiumExtract | Out-Null
tar.exe -xzf $pdfiumArchive -C $pdfiumExtract
$pdfium = Join-Path $Destination 'tools/pdfium'
New-Item -ItemType Directory -Force $pdfium | Out-Null
Copy-Item -LiteralPath (Join-Path $pdfiumExtract 'bin/pdfium.dll') -Destination $pdfium -Force
Copy-Notices $pdfiumExtract 'pdfium'
Get-RepositoryFile $lock.pdfium.repository $pdfiumCommit 'LICENSE' (Join-Path $notices 'pdfium/packaging-LICENSE.txt')

$qpdfArchive = Get-ReleaseAsset $lock.qpdf $lock.qpdf.sha256
$qpdfExtract = Join-Path $Cache 'qpdf-12.4.2-msvc64'
Expand-Archive -LiteralPath $qpdfArchive -DestinationPath $Cache -Force
$qpdfBin = Join-Path $qpdfExtract 'bin'
$qpdf = Join-Path $Destination 'tools/qpdf'
New-Item -ItemType Directory -Force $qpdf | Out-Null
Copy-Item -LiteralPath (Join-Path $qpdfBin 'qpdf.exe') -Destination $qpdf -Force
Get-ChildItem -LiteralPath $qpdfBin -Filter '*.dll' -File | Copy-Item -Destination $qpdf -Force
Copy-Notices $qpdfExtract 'qpdf'

$vcpkg = Join-Path $Cache "vcpkg-$($lock.vcpkg.tag)"
if (!(Test-Path -LiteralPath (Join-Path $vcpkg '.git'))) {
    git clone --depth 1 --branch $lock.vcpkg.tag $lock.vcpkg.repository $vcpkg
}
$revision = (git -C $vcpkg rev-parse HEAD).Trim()
$remoteRevision = Get-Commit 'microsoft/vcpkg' $lock.vcpkg.tag ''
if ($revision -cne $remoteRevision -or (git -C $vcpkg status --porcelain --untracked-files=no)) {
    throw 'vcpkg checkout differs from the pinned release.'
}
& (Join-Path $vcpkg 'bootstrap-vcpkg.bat') -disableMetrics
& (Join-Path $vcpkg 'vcpkg.exe') install "tesseract[core]:$($lock.vcpkg.triplet)" --classic --disable-metrics
$port = Get-Content -Raw (Join-Path $vcpkg 'ports/tesseract/vcpkg.json') | ConvertFrom-Json -AsHashtable
$tessVersion = @($port['version'], $port['version-semver'], $port['version-string'] | Where-Object { $_ })[0]
if ($tessVersion -cne $lock.vcpkg.tesseractVersion) { throw 'Unexpected Tesseract port version.' }
$installed = Join-Path $vcpkg "installed/$($lock.vcpkg.triplet)"
$installedStatus = Get-Content -Raw -LiteralPath (Join-Path $vcpkg 'installed/vcpkg/status')
if ($installedStatus -match '(?mi)^Package: (jbigkit|libjbig|jbig2dec)?$') {
    throw 'The release OCR build requires a dependency closure without JBIG-KIT or jbig2dec.'
}
$png = @($installedStatus -split '(?:?
){2,}' |
    Where-Object { $_ -match '(?m)^Package: libpng?$' -and
        $_ -match ('(?m)^Architecture: ' + [regex]::Escape($lock.vcpkg.triplet) + '?$') -and
        $_ -match ('(?m)^Version: ' + [regex]::Escape($lock.vcpkg.libpngVersion) + '(?:#[0-9]+)??$') })
if ($png.Count -ne 1) {
    throw "Expected libpng $($lock.vcpkg.libpngVersion) in the Windows OCR dependency tree."
}
$cli = @(Get-ChildItem -LiteralPath (Join-Path $installed 'tools/tesseract') -Recurse -Filter 'tesseract.exe' -File)
if ($cli.Count -ne 1) { throw 'The locked vcpkg port must provide one statically linked Tesseract CLI.' }
$tess = Join-Path $Destination 'tools/tesseract'
$data = Join-Path $tess 'tessdata'
$config = Join-Path $data 'configs'
New-Item -ItemType Directory -Force $tess, $data, $config | Out-Null
Copy-Item -LiteralPath $cli[0].FullName -Destination $tess -Force
$provenance['tesseract.exe'] = @{ vcpkgTag = $lock.vcpkg.tag; vcpkgCommit = $revision; triplet = $lock.vcpkg.triplet; version = $tessVersion; libpng = $lock.vcpkg.libpngVersion }
Copy-Notices (Join-Path $installed 'share') 'vcpkg'
$models = Get-Commit $lock.tessdata.repository $lock.tessdata.tag $lock.tessdata.commitPrefix
foreach ($model in @('eng.traineddata', 'deu.traineddata')) {
    Get-RepositoryFile $lock.tessdata.repository $models $model (Join-Path $data $model)
}
New-Item -ItemType Directory -Force (Join-Path $notices 'tessdata') | Out-Null
Get-RepositoryFile $lock.tessdata.repository $models 'LICENSE' (Join-Path $notices 'tessdata/LICENSE')
$tessCommit = Get-Commit 'tesseract-ocr/tesseract' $tessVersion ''
Get-RepositoryFile 'tesseract-ocr/tesseract' $tessCommit 'tessdata/pdf.ttf' (Join-Path $data 'pdf.ttf')
Get-RepositoryFile 'tesseract-ocr/tesseract' $tessCommit 'tessdata/configs/pdf' (Join-Path $config 'pdf')

# App-local VC++ runtime DLLs come from the redistributable folder of a licensed VS installation.
if (!$CrtDirectory) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (!$vs) { throw 'Install Visual Studio Build Tools with MSVC x64 and C++ redist or supply -CrtDirectory.' }
    $vc = Join-Path ($vs | Select-Object -First 1) 'VC/Redist/MSVC'
    $CrtDirectory = Get-ChildItem -LiteralPath $vc -Directory |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64/Microsoft.VC143.CRT' } |
        Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$CrtDirectory -or !(Test-Path -LiteralPath (Join-Path $CrtDirectory 'vcruntime140.dll'))) {
    throw 'The x64 app-local Microsoft.VC143.CRT directory is required.'
}
$crtFiles = @(Get-ChildItem -LiteralPath $CrtDirectory -Filter '*.dll' -File)
foreach ($dll in $crtFiles) {
    foreach ($target in @($Destination, $qpdf, $pdfium, $tess)) {
        Copy-Item -LiteralPath $dll.FullName -Destination $target -Force
    }
}
$provenance['msvc-crt'] = @{ version = (Get-Item -LiteralPath (Join-Path $CrtDirectory 'vcruntime140.dll')).VersionInfo.FileVersion }
$provenance | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $Destination 'NATIVE-ORIGINS.json') -Encoding utf8

& (Join-Path $qpdf 'qpdf.exe') --version
& (Join-Path $tess 'tesseract.exe') --version
& (Join-Path $tess 'tesseract.exe') --tessdata-dir $data --list-langs
