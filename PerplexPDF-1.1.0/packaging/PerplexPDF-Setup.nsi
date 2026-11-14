Unicode true
Target amd64-unicode
RequestExecutionLevel user
SetCompressor /SOLID lzma
CRCCheck force
ShowInstDetails show

!include "MUI2.nsh"
!include "LogicLib.nsh"

!ifndef PAYLOAD_DIR
    !error "Define PAYLOAD_DIR as the directory containing the Windows payload."
!endif
!ifndef SETUP_EXE
    !error "Define SETUP_EXE as the output path for the installer."
!endif
!ifndef PRODUCT_VERSION
    !define PRODUCT_VERSION "1.1.0"
!endif

Name "PerplexPDF ${PRODUCT_VERSION}"
OutFile "${SETUP_EXE}"
InstallDir "$LOCALAPPDATA\Programs\PerplexPDF"
VIProductVersion "${PRODUCT_VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "PerplexPDF"
VIAddVersionKey /LANG=1033 "CompanyName" "Michael Lappenbusch"
VIAddVersionKey /LANG=1033 "FileDescription" "PerplexPDF Windows Setup"
VIAddVersionKey /LANG=1033 "FileVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${PRODUCT_VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 Michael Lappenbusch"

!define MUI_ICON "..\src\PerplexPDF.App\Resources\PerplexPDF.ico"
!define MUI_UNICON "..\src\PerplexPDF.App\Resources\PerplexPDF.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\PerplexPDF.exe"
!define MUI_FINISHPAGE_RUN_FUNCTION LaunchPerplexPDF
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "German"
!insertmacro MUI_LANGUAGE "English"

LangString InstallFailed ${LANG_GERMAN} "PerplexPDF konnte nicht installiert werden. Die Details im Setup-Fenster zeigen den Fehler. Schließe PerplexPDF und versuche es erneut."
LangString InstallFailed ${LANG_ENGLISH} "PerplexPDF could not be installed. See the details in the setup window. Close PerplexPDF and try again."
LangString UninstallFailed ${LANG_GERMAN} "PerplexPDF konnte nicht deinstalliert werden. Die Details im Setup-Fenster zeigen den Fehler."
LangString UninstallFailed ${LANG_ENGLISH} "PerplexPDF could not be uninstalled. See the details in the setup window."

Section "PerplexPDF"
    SetShellVarContext current
    InitPluginsDir
    SetOutPath "$PLUGINSDIR\payload"
    File /r "${PAYLOAD_DIR}\*"
    ClearErrors
    WriteUninstaller "$PLUGINSDIR\payload\Uninstall-PerplexPDF.exe"
    ${If} ${Errors}
        MessageBox MB_OK|MB_ICONSTOP "$(InstallFailed)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}

    ; The same transactional installer handles upgrades from the portable package.
    nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "$PLUGINSDIR\payload\Setup-PerplexPDF.ps1" -Quiet -NativeSetup'
    Pop $0
    ${If} $0 != "0"
        DetailPrint "Setup returned: $0"
        MessageBox MB_OK|MB_ICONSTOP "$(InstallFailed)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
SectionEnd

Function LaunchPerplexPDF
    ; Setup installs for the current user in this fixed folder.
    StrCpy $INSTDIR "$LOCALAPPDATA\Programs\PerplexPDF"
    SetOutPath "$INSTDIR"
    Exec '"$INSTDIR\PerplexPDF.exe"'
FunctionEnd

Section "Uninstall"
    SetShellVarContext current
    InitPluginsDir
    SetOutPath "$PLUGINSDIR"
    nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoLogo -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File "$LOCALAPPDATA\Programs\PerplexPDF\Setup-PerplexPDF.ps1" -Uninstall -Quiet'
    Pop $0
    ${If} $0 != "0"
        DetailPrint "Uninstall returned: $0"
        MessageBox MB_OK|MB_ICONSTOP "$(UninstallFailed)" /SD IDOK
        SetErrorLevel 1
        Abort
    ${EndIf}
SectionEnd
