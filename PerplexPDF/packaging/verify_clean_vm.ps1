#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Archive,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$Sha256,
    [int]$WaitSeconds = 25
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Archive = (Resolve-Path -LiteralPath $Archive).Path
if ((Get-FileHash -Algorithm SHA256 -LiteralPath $Archive).Hash -ine $Sha256) {
    throw 'Portable archive hash does not match.'
}
$root = Join-Path $env:LOCALAPPDATA ('PerplexPDF acceptance ' + [guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $Archive -DestinationPath $root
$expected = @(
    'PerplexPDF.exe', 'PerplexPDF.dll', 'PerplexPDF.deps.json', 'PerplexPDF.runtimeconfig.json',
    'hostfxr.dll', 'hostpolicy.dll', 'coreclr.dll', 'PresentationFramework.dll', 'PresentationCore.dll',
    'example.pdf', 'tools/pdfium/pdfium.dll', 'tools/qpdf/qpdf.exe',
    'tools/tesseract/tesseract.exe', 'tools/tesseract/tessdata/eng.traineddata',
    'tools/tesseract/tessdata/deu.traineddata', 'tools/tesseract/tessdata/pdf.ttf',
    'LICENSE', 'THIRD_PARTY_NOTICES.txt', 'NATIVE-SHA256SUMS.txt', 'Setup-PerplexPDF.cmd', 'Setup-PerplexPDF.ps1'
)
foreach ($file in $expected) {
    if (!(Test-Path -LiteralPath (Join-Path $root $file) -PathType Leaf)) { throw "Missing packaged file: $file" }
}
foreach ($record in Get-Content -LiteralPath (Join-Path $root 'NATIVE-SHA256SUMS.txt')) {
    if ($record -notmatch '^([0-9a-f]{64})  (.+)$') { throw 'Invalid file hash record.' }
    $digest = $Matches[1]
    $file = Join-Path $root $Matches[2]
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $file).Hash -ine $digest) { throw "File hash mismatch: $file" }
}
& (Join-Path $root 'tools/qpdf/qpdf.exe') --check (Join-Path $root 'example.pdf')
if ($LASTEXITCODE) { throw 'Packaged qpdf failed.' }
$languages = & (Join-Path $root 'tools/tesseract/tesseract.exe') --tessdata-dir (Join-Path $root 'tools/tesseract/tessdata') --list-langs
if ($LASTEXITCODE -or 'eng' -notin $languages -or 'deu' -notin $languages) { throw 'Packaged OCR languages are unavailable.' }
$inputPath = '"' + (Join-Path $root 'example.pdf') + '"'
$app = Start-Process -FilePath (Join-Path $root 'PerplexPDF.exe') -ArgumentList $inputPath -WorkingDirectory $root -PassThru
$watch = [Diagnostics.Stopwatch]::StartNew()
do {
    Start-Sleep -Milliseconds 200
    $app.Refresh()
    if ($app.HasExited) { throw "App exited during startup with code $($app.ExitCode)." }
    if ($app.MainWindowTitle -ceq 'PerplexPDF error') {
        throw 'PerplexPDF showed an error dialog. See %LOCALAPPDATA%\PerplexPDF\startup.log.'
    }
} while (!$app.MainWindowHandle -and $watch.Elapsed.TotalSeconds -lt $WaitSeconds)
if (!$app.MainWindowHandle) { throw 'PerplexPDF did not show its main window within the test interval.' }
Write-Host ("Started window in {0:N1}s: {1}" -f $watch.Elapsed.TotalSeconds, $app.MainWindowTitle)
Write-Host "Inspect the open window and continue with RELEASE_CHECKLIST.md. Extracted package: $root"
