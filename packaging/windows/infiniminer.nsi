; Windows installer for Infiniminer. Built by packaging/package.sh:
;   makensis -DVERSION=1.9 -DSRCDIR=<published folder> -DOUTFILE=<installer.exe> infiniminer.nsi
; Installed copies keep their settings, ban lists and saved maps in %APPDATA%\Infiniminer,
; because Program Files can't be written to.

Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "dev"
!endif
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\Infiniminer"

Name "Infiniminer ${VERSION}"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\Infiniminer"
InstallDirRegKey HKLM "Software\Infiniminer" "Install_Dir"
RequestExecutionLevel admin
SetCompressor /SOLID lzma

!define MUI_ICON "${SRCDIR}\Game.ico"
!define MUI_UNICON "${SRCDIR}\Game.ico"
!define MUI_FINISHPAGE_RUN "$INSTDIR\Infiniminer.exe"
!define MUI_FINISHPAGE_RUN_TEXT "Play Infiniminer"

!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"

Section "Infiniminer (required)" SecMain
  SectionIn RO
  SetShellVarContext all
  SetOutPath $INSTDIR
  ; A reinstall over an older version starts clean, so no stale files are left behind.
  RMDir /r "$INSTDIR\Content"
  File /r "${SRCDIR}\*.*"

  WriteRegStr HKLM "Software\Infiniminer" "Install_Dir" "$INSTDIR"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayName" "Infiniminer"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "Publisher" "Zachtronics Industries"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\Game.ico"
  WriteRegStr HKLM "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTALL_KEY}" "NoRepair" 1
  WriteUninstaller "$INSTDIR\uninstall.exe"
SectionEnd

Section "Start Menu shortcuts" SecStartMenu
  SetShellVarContext all
  CreateDirectory "$SMPROGRAMS\Infiniminer"
  CreateShortCut "$SMPROGRAMS\Infiniminer\Infiniminer.lnk" "$INSTDIR\Infiniminer.exe" "" "$INSTDIR\Game.ico"
  CreateShortCut "$SMPROGRAMS\Infiniminer\Infiniminer Server.lnk" "$INSTDIR\InfiniminerServer.exe" "" "$INSTDIR\Game.ico"
  ; Each player's own settings folder; cmd expands %APPDATA% for whoever opens the shortcut.
  CreateShortCut "$SMPROGRAMS\Infiniminer\Settings Folder.lnk" "$SYSDIR\cmd.exe" \
    '/c mkdir "%APPDATA%\Infiniminer" 2>nul & start "" "%APPDATA%\Infiniminer"' "$SYSDIR\shell32.dll" 3 SW_SHOWMINIMIZED
  CreateShortCut "$SMPROGRAMS\Infiniminer\README.lnk" "notepad.exe" "$INSTDIR\README.txt"
  CreateShortCut "$SMPROGRAMS\Infiniminer\Uninstall.lnk" "$INSTDIR\uninstall.exe"
SectionEnd

Section /o "Desktop shortcut" SecDesktop
  SetShellVarContext all
  CreateShortCut "$DESKTOP\Infiniminer.lnk" "$INSTDIR\Infiniminer.exe" "" "$INSTDIR\Game.ico"
SectionEnd

Section "Uninstall"
  SetShellVarContext all
  ; Settings in %APPDATA%\Infiniminer are left alone.
  Delete "$DESKTOP\Infiniminer.lnk"
  RMDir /r "$SMPROGRAMS\Infiniminer"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKLM "${UNINSTALL_KEY}"
  DeleteRegKey HKLM "Software\Infiniminer"
SectionEnd
