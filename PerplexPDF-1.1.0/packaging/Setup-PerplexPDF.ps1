#Requires -Version 5.1
[CmdletBinding()]
param([switch]$Uninstall, [switch]$Quiet, [switch]$NativeSetup)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$programs = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs'
$install = Join-Path $programs 'PerplexPDF'
$exe = Join-Path $install 'PerplexPDF.exe'
$marker = 'PerplexPDF.installed'
$manifest = 'SHA256SUMS.txt'
$nativeUninstaller = 'Uninstall-PerplexPDF.exe'
$capabilities = 'Software\PerplexPDF\Capabilities'
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\PerplexPDF'
$registered = 'Software\RegisteredApplications'
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'PerplexPDF.lnk'
$associations = [ordered]@{ '.pdf' = 'PerplexPDF.PDF'; '.ppdf' = 'PerplexPDF.Project' }
$registry = [Microsoft.Win32.Registry]::CurrentUser

function Assert-PlainPath([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
    if ($null -ne $item -and $item.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw "Setup cannot use a redirected path: $Path"
    }
}

function Get-PayloadPath([string]$Root, [string]$Name) {
    $path = $Root
    foreach ($part in $Name.Split('/')) {
        if (!$part -or $part -in '.', '..' -or $part.EndsWith('.') -or $part.EndsWith(' ')) {
            throw 'Invalid relative path in the package manifest.'
        }
        $path = Join-Path $path $part
        Assert-PlainPath $path
    }
    return $path
}

function Read-Payload([string]$Root) {
    $hashes = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($line in Get-Content -LiteralPath (Get-PayloadPath $Root $manifest) -Encoding UTF8) {
        if ($line -notmatch '^([0-9a-f]{64})  ([a-z0-9_][a-z0-9_./ +\-]*)$') {
            throw 'Invalid package manifest. Extract the complete ZIP and try again.'
        }
        $hashes.Add($Matches[2], $Matches[1])
    }
    foreach ($name in 'PerplexPDF.exe', 'PerplexPDF.dll', 'PerplexPDF.runtimeconfig.json', 'Setup-PerplexPDF.ps1') {
        if (!$hashes.ContainsKey($name)) { throw "Incomplete package manifest: $name" }
    }
    return ,$hashes
}

function Assert-ManagedInstall {
    Assert-PlainPath $install
    $owner = Get-PayloadPath $install $marker
    if (!(Test-Path -LiteralPath $owner -PathType Leaf) -or (Get-Content -LiteralPath $owner -Raw).Trim() -cne 'PerplexPDF setup v1') {
        throw "Setup does not manage this folder: $install"
    }
}

function Set-RegValue([string]$Path, [string]$Name, [object]$Value,
        [Microsoft.Win32.RegistryValueKind]$Kind = [Microsoft.Win32.RegistryValueKind]::String) {
    $key = $registry.CreateSubKey($Path)
    try { $key.SetValue($Name, $Value, $Kind) }
    finally { $key.Dispose() }
}

function Remove-RegValue([string]$Path, [string]$Name) {
    $key = $registry.OpenSubKey($Path, $true)
    if ($null -ne $key) {
        try { $key.DeleteValue($Name, $false) }
        finally { $key.Dispose() }
    }
}

function Register-PerplexPDF {
    $command = '"{0}" "%1"' -f $exe
    $icon = '"{0}",0' -f $exe
    foreach ($extension in $associations.Keys) {
        $id = $associations[$extension]
        $class = "Software\Classes\$id"
        Set-RegValue $class '' $(if ($extension -eq '.pdf') { 'PDF document' } else { 'PerplexPDF project' })
        Set-RegValue "$class\DefaultIcon" '' $icon
        Set-RegValue "$class\shell\open\command" '' $command
        Set-RegValue "Software\Classes\$extension\OpenWithProgids" $id ''
        Set-RegValue "$capabilities\FileAssociations" $extension $id
    }
    Set-RegValue $capabilities 'ApplicationName' 'PerplexPDF'
    Set-RegValue $capabilities 'ApplicationDescription' 'Open, edit, and export PDF documents.'
    Set-RegValue $capabilities 'ApplicationIcon' $icon
    Set-RegValue $registered 'PerplexPDF' $capabilities

    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $exe
    $link.WorkingDirectory = $install
    $link.Save()

    $powerShell = Join-Path $PSHOME 'powershell.exe'
    $script = Join-Path $install 'Setup-PerplexPDF.ps1'
    Set-RegValue $uninstallKey 'DisplayName' 'PerplexPDF'
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $install 'PerplexPDF.dll'))
    Set-RegValue $uninstallKey 'DisplayVersion' ('{0}.{1}.{2}' -f $version.FileMajorPart, $version.FileMinorPart, $version.FileBuildPart)
    Set-RegValue $uninstallKey 'Publisher' 'Michael Lappenbusch'
    Set-RegValue $uninstallKey 'DisplayIcon' $icon
    Set-RegValue $uninstallKey 'InstallLocation' $install
    $native = Join-Path $install $nativeUninstaller
    if (Test-Path -LiteralPath $native -PathType Leaf) {
        Set-RegValue $uninstallKey 'UninstallString' ('"{0}"' -f $native)
        Set-RegValue $uninstallKey 'QuietUninstallString' ('"{0}" /S' -f $native)
    }
    else {
        Set-RegValue $uninstallKey 'UninstallString' ('"{0}" -NoProfile -ExecutionPolicy Bypass -File "{1}" -Uninstall -Quiet' -f $powerShell, $script)
        Remove-RegValue $uninstallKey 'QuietUninstallString'
    }
    Set-RegValue $uninstallKey 'NoModify' 1 DWord
    Set-RegValue $uninstallKey 'NoRepair' 1 DWord
}

function Unregister-PerplexPDF {
    Remove-RegValue $registered 'PerplexPDF'
    foreach ($extension in $associations.Keys) {
        $id = $associations[$extension]
        Remove-RegValue "Software\Classes\$extension\OpenWithProgids" $id
        $registry.DeleteSubKeyTree("Software\Classes\$id", $false)
    }
    $registry.DeleteSubKeyTree($capabilities, $false)
    $registry.DeleteSubKeyTree($uninstallKey, $false)
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
}

function Notify-AssociationsChanged {
    try {
        Add-Type -Namespace PerplexPDF -Name Shell -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, System.IntPtr first, System.IntPtr second);
'@
        [PerplexPDF.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch { Write-Warning 'Windows Explorer may refresh file associations after the next sign-in.' }
}

function Remove-PreviousPackage([string]$Root, $Hashes) {
    try {
        $directories = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        $null = $directories.Add($Root)
        foreach ($name in $Hashes.Keys) {
            $path = Get-PayloadPath $Root $name
            if ((Test-Path -LiteralPath $path -PathType Leaf) -and
                (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ieq $Hashes[$name]) {
                Remove-Item -LiteralPath $path -Force
            }
            $parent = [IO.Path]::GetDirectoryName($path)
            while ($parent -ne $Root) {
                $null = $directories.Add($parent)
                $parent = [IO.Path]::GetDirectoryName($parent)
            }
        }
        foreach ($name in $marker, $manifest) { Remove-Item -LiteralPath (Get-PayloadPath $Root $name) -Force }
        foreach ($directory in $directories | Sort-Object -Property Length -Descending) {
            if ((Test-Path -LiteralPath $directory -PathType Container) -and
                !(Get-ChildItem -LiteralPath $directory -Force | Select-Object -First 1)) {
                Remove-Item -LiteralPath $directory -Force
            }
        }
    }
    catch { Write-Warning $_.Exception.Message }
    if (Test-Path -LiteralPath $Root) { Write-Host "Extra or modified files were retained in $Root" }
}

if (![Environment]::Is64BitOperatingSystem) { throw 'This package requires 64-bit Windows.' }
Assert-PlainPath $programs
$null = [IO.Directory]::CreateDirectory($programs)
$lock = [IO.File]::Open((Get-PayloadPath $programs 'PerplexPDF.setup.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
    if (Get-Process -Name 'PerplexPDF' -ErrorAction SilentlyContinue) {
        throw 'Close PerplexPDF before installing or uninstalling it.'
    }
    $previous = $null
    if (Test-Path -LiteralPath $install) {
        Assert-ManagedInstall
        $previous = Read-Payload $install
    }
    $backup = Join-Path $programs ('PerplexPDF.previous-' + [Guid]::NewGuid().ToString('N'))
    if ($Uninstall) {
        if ($null -eq $previous) { throw 'PerplexPDF is not installed for this user.' }
        if (!$Quiet -and (Read-Host 'Uninstall PerplexPDF for this user? [y/N]') -notmatch '^(y|yes)$') { return }
        [IO.Directory]::Move($install, $backup)
        try { Unregister-PerplexPDF }
        catch {
            [IO.Directory]::Move($backup, $install)
            Register-PerplexPDF
            throw
        }
        Remove-PreviousPackage $backup $previous
        Write-Host 'PerplexPDF was uninstalled. Your documents and recovery data were retained.'
    }
    else {
        if ($PSScriptRoot -ieq $install) { throw 'Run setup from the newly extracted ZIP folder.' }
        $hashes = Read-Payload $PSScriptRoot
        $stage = Join-Path $programs ('PerplexPDF.setup-' + [Guid]::NewGuid().ToString('N'))
        Write-Host "Installing PerplexPDF for the current user in $install"
        $null = [IO.Directory]::CreateDirectory($stage)
        $published = $false
        try {
            foreach ($name in $hashes.Keys) {
                $source = Get-PayloadPath $PSScriptRoot $name
                $target = Get-PayloadPath $stage $name
                $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
                [IO.File]::Copy($source, $target)
                if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ine $hashes[$name]) {
                    throw "File hash mismatch: $name"
                }
            }
            $stagedManifest = Get-PayloadPath $stage $manifest
            Copy-Item -LiteralPath (Get-PayloadPath $PSScriptRoot $manifest) -Destination $stagedManifest
            if ($NativeSetup) {
                $uninstaller = Get-PayloadPath $stage $nativeUninstaller
                [IO.File]::Copy((Get-PayloadPath $PSScriptRoot $nativeUninstaller), $uninstaller)
                $uninstallerHash = (Get-FileHash -LiteralPath $uninstaller -Algorithm SHA256).Hash.ToLowerInvariant()
                Add-Content -LiteralPath $stagedManifest -Value "$uninstallerHash  $nativeUninstaller" -Encoding ascii
            }
            Set-Content -LiteralPath (Join-Path $stage $marker) -Value 'PerplexPDF setup v1' -Encoding ASCII
            if ($null -ne $previous) { [IO.Directory]::Move($install, $backup) }
            [IO.Directory]::Move($stage, $install)
            $published = $true
            Register-PerplexPDF
        }
        catch {
            if ($published) { [IO.Directory]::Move($install, $stage) }
            if (Test-Path -LiteralPath $backup) {
                [IO.Directory]::Move($backup, $install)
                Register-PerplexPDF
            }
            elseif ($published) { Unregister-PerplexPDF }
            throw
        }
        finally {
            if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
        }
        if ($null -ne $previous) { Remove-PreviousPackage $backup $previous }
        Write-Host 'PerplexPDF is installed. It is available in Start and Open with.'
    }
    Notify-AssociationsChanged
}
finally { $lock.Dispose() }

if (!$Uninstall -and !$Quiet -and (Read-Host 'Open Windows Default apps to choose PerplexPDF for .pdf? [Y/n]') -notmatch '^(n|no)$') {
    try { Start-Process 'ms-settings:defaultapps?registeredAppUser=PerplexPDF' }
    catch {
        Write-Host 'Open Settings > Apps > Default apps and search for .pdf.'
        $null = Read-Host 'Press Enter to close setup'
    }
}
