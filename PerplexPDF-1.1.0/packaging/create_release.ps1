#Requires -Version 7.4
[CmdletBinding()]
param(
    [string]$CrtDirectory,
    [string]$ApprovedAcceptance,
    [string]$MakeNsis
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if (!$IsWindows -or [Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne [Runtime.InteropServices.Architecture]::X64) {
    throw 'Use an x64 Windows build machine with .NET 10 SDK, MSVC, git and PowerShell 7.'
}
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifacts = Join-Path $repo 'artifacts'
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $repo 'src/PerplexPDF.App/PerplexPDF.App.csproj'))).Project.PropertyGroup.Version
$name = "PerplexPDF-$version-win-x64-portable.zip"
$setupName = "PerplexPDF-$version-Setup-win-x64.exe"
$candidate = Join-Path $artifacts 'candidate'

if ($ApprovedAcceptance) {
    $report = Get-Content -Raw -LiteralPath $ApprovedAcceptance | ConvertFrom-Json
    $archive = Join-Path $candidate $name
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    $required = 1..27 | Where-Object { $_ -ne 23 } | ForEach-Object { 'T-{0:000}' -f $_ }
    if ($report.sha256 -ine $hash -or !$report.reviewer -or !$report.windowsBuild -or !$report.date -or
        $report.cleanWindows11 -ne $true -or $report.offline -ne $true -or $report.licensesApproved -ne $true -or
        @($required | Where-Object { $_ -notin $report.passed }).Count -ne 0) {
        throw 'The acceptance report must identify the reviewed SHA-256 and record every required test as passed on the clean Windows configuration.'
    }
    $release = Join-Path $artifacts 'release'
    New-Item -ItemType Directory -Force $release | Out-Null
    Copy-Item -LiteralPath $archive, $ApprovedAcceptance -Destination $release -Force
    "$hash  $name" | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Approved portable archive: $release"
    return
}

Push-Location $repo
try {
    if (!$MakeNsis) {
        $command = Get-Command makensis.exe -ErrorAction SilentlyContinue
        if ($command) { $MakeNsis = $command.Source }
        else {
            $standard = Join-Path ${env:ProgramFiles(x86)} 'NSIS/makensis.exe'
            if (Test-Path -LiteralPath $standard) { $MakeNsis = $standard }
        }
    }
    $run = Join-Path $artifacts ("build-" + [guid]::NewGuid().ToString('N'))
    $publish = Join-Path $run 'publish'
    New-Item -ItemType Directory -Force $publish | Out-Null
    $app = 'src/PerplexPDF.App/PerplexPDF.App.csproj'
    dotnet --version
    dotnet restore PerplexPDF.sln '-p:WarningsAsErrors=NU1903%3BNU1904'
    dotnet restore $app --runtime win-x64 '-p:SelfContained=true' '-p:WarningsAsErrors=NU1903%3BNU1904'
    dotnet restore $app --runtime win-x64 '--locked-mode' '-p:SelfContained=true'
    $publishArgs = @('publish', $app, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '--no-restore',
        '-p:PublishSingleFile=false', '-p:PublishTrimmed=false', '-p:DebugType=embedded', '-o', $publish)
    & dotnet @publishArgs

    & (Join-Path $PSScriptRoot 'prepare_native.ps1') -Destination $publish -Cache (Join-Path $artifacts 'deps') -CrtDirectory $CrtDirectory
    $licenses = Join-Path $publish 'licenses'
    Copy-Item -LiteralPath 'licenses/DejaVu.txt' -Destination $licenses
    if ($MakeNsis) { Copy-Item -LiteralPath 'licenses/NSIS.txt' -Destination $licenses }
    Copy-Item -LiteralPath 'DEPENDENCIES.md', 'docs/USER_GUIDE_EN.md', 'docs/LINUX_BUILD.md', 'docs/MACOS_BUILD.md', 'docs/CHANGELOG.md', 'docs/RELEASE_CHECKLIST.md', 'docs/WINDOWS_BUILD.md' -Destination $publish
    Copy-Item -LiteralPath 'docs/README_PORTABLE.txt' -Destination (Join-Path $publish 'README.txt')
    Copy-Item -LiteralPath 'tests/Fixtures/example.pdf' -Destination $publish
    Copy-Item -LiteralPath 'tests/Fixtures/scanned-en-de.pdf' -Destination (Join-Path $publish 'ocr-scan-en-de.pdf')
    $examples = Join-Path $publish 'examples'
    New-Item -ItemType Directory -Force $examples | Out-Null
    Copy-Item -LiteralPath 'tests/Fixtures/word-layout.pdf', 'tests/Fixtures/epub-structure.pdf' -Destination $examples
    Copy-Item -LiteralPath 'packaging/Setup-PerplexPDF.cmd', 'packaging/Setup-PerplexPDF.ps1' -Destination $publish
    Copy-Item -LiteralPath 'packaging/verify_clean_vm.ps1' -Destination $publish

    $assets = Get-Content -Raw -LiteralPath 'src/PerplexPDF.App/obj/project.assets.json' | ConvertFrom-Json -AsHashtable
    $runtimeConfig = Get-Content -Raw -LiteralPath (Join-Path $publish 'PerplexPDF.runtimeconfig.json') | ConvertFrom-Json
    $runtime = $runtimeConfig.runtimeOptions.includedFrameworks |
        Where-Object name -eq 'Microsoft.NETCore.App' | Select-Object -ExpandProperty version
    $packages = @("skiasharp/3.119.2", "skiasharp.nativeassets.win32/3.119.2",
        "microsoft.netcore.app.runtime.win-x64/$runtime", "microsoft.windowsdesktop.app.runtime.win-x64/$runtime")
    foreach ($entry in $packages) {
        $folder = $assets.packageFolders.Keys |
            ForEach-Object { Join-Path $_ $entry } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        if (!$folder) { throw "Could not locate the restored redistributable package $entry" }
        $destination = Join-Path $licenses ($entry -replace '/', '-')
        New-Item -ItemType Directory -Force $destination | Out-Null
        $files = @(Get-ChildItem -LiteralPath $folder -Recurse -File |
            Where-Object { $_.Name -match '(?i)license|notice|copyright|copying' -and $_.Extension -notin '.exe','.dll','.lib','.pdb' })
        if (!$files.Count) { throw "Inspect $entry and supply the upstream redistribution notices before packaging." }
        foreach ($file in $files) {
            $relative = [IO.Path]::GetRelativePath($folder, $file.FullName)
            $target = Join-Path $destination $relative
            New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target
        }
    }
    $lockFolder = Join-Path $publish 'licenses/build-locks'
    New-Item -ItemType Directory -Force $lockFolder | Out-Null
    Get-ChildItem -Path 'src', 'tests' -Recurse -Filter 'packages.lock.json' -File |
        ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $lockFolder ($_.Directory.Name + '.json'))
        }
    Copy-Item -LiteralPath 'packaging/native-lock.json' -Destination $lockFolder
    Get-ChildItem -LiteralPath $publish -File -Recurse |
        Where-Object { $_.Extension -in '.exe','.dll','.ttf','.traineddata' } |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "$hash  $([IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'))"
        } | Set-Content -LiteralPath (Join-Path $publish 'NATIVE-SHA256SUMS.txt') -Encoding ascii
    Get-ChildItem -LiteralPath $publish -File -Recurse |
        Sort-Object FullName |
        ForEach-Object {
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "$hash  $([IO.Path]::GetRelativePath($publish, $_.FullName).Replace('\', '/'))"
        } | Set-Content -LiteralPath (Join-Path $publish 'SHA256SUMS.txt') -Encoding ascii

    $env:PERPLEXPDF_PDFIUM = Join-Path $publish 'tools/pdfium/pdfium.dll'
    $env:PERPLEXPDF_QPDF = Join-Path $publish 'tools/qpdf/qpdf.exe'
    $env:PERPLEXPDF_TESSERACT = Join-Path $publish 'tools/tesseract/tesseract.exe'
    $env:PERPLEXPDF_TESSDATA = Join-Path $publish 'tools/tesseract/tessdata'
    dotnet test tests/PerplexPDF.UnitTests -c Release --no-restore --logger 'console;verbosity=normal'
    dotnet test tests/PerplexPDF.IntegrationTests -c Release --no-restore --logger 'console;verbosity=normal'

    $archive = Join-Path $run $name
    [IO.Compression.ZipFile]::CreateFromDirectory($publish, $archive, [IO.Compression.CompressionLevel]::Optimal, $false)
    $unpacked = Join-Path $run 'extracted smoke'
    [IO.Compression.ZipFile]::ExtractToDirectory($archive, $unpacked)
    $env:PERPLEXPDF_RELEASE = $unpacked
    dotnet test tests/PerplexPDF.WindowsSmokeTests -c Release --no-restore --logger 'console;verbosity=normal'
    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archive).Hash.ToLowerInvariant()
    New-Item -ItemType Directory -Force $candidate | Out-Null
    Copy-Item -LiteralPath $archive -Destination $candidate -Force
    $checksums = @("$hash  $name")
    if ($MakeNsis) {
        $setup = Join-Path $run $setupName
        & $MakeNsis '/V3' "/DPAYLOAD_DIR=$publish" "/DSETUP_EXE=$setup" "/DPRODUCT_VERSION=$version" (Join-Path $PSScriptRoot 'PerplexPDF-Setup.nsi')
        Copy-Item -LiteralPath $setup -Destination $candidate -Force
        $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash.ToLowerInvariant()
        $checksums += "$setupHash  $setupName"
    }
    $checksums | Set-Content -LiteralPath (Join-Path $candidate 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Candidate: $candidate. Published files including PerplexPDF.exe: $publish"
    Write-Host 'Run packaging/verify_clean_vm.ps1 and record the remaining acceptance results before release.'
}
finally { Pop-Location }
