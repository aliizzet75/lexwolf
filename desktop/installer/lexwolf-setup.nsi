; Build: ./release.sh <version z.B. 0.47.0> "<notes>"
; Das Skript haelt LexWolf.csproj <Version>, backend/api/client_update.py
; VERSION_HISTORY und diesen Installer synchron (nicht mehr einzeln von Hand
; pflegen — das lief vorher auseinander und loeste eine Update-Dialog-
; Endlosschleife beim Client aus).

!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif

Name "LexWolf"
OutFile "\out\LexWolf-Setup.exe"
InstallDir "$PROGRAMFILES64\LexWolf"
RequestExecutionLevel admin

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "German"

Section "Install"
  ; Laufende Instanz beenden — nötig für Silent-Self-Update (App startet den
  ; Installer selbst und beendet sich, aber taskkill hier macht es robust
  ; unabhängig vom Timing; harmlos falls nichts läuft).
  nsExec::Exec 'taskkill /F /IM LexWolf.exe /T'
  Sleep 500

  SetOutPath "$INSTDIR"
  File "\build\lexwolf-desktop\LexWolf.exe"
  ; Native Whisper-DLLs (Spracheingabe) — liegen bewusst neben der .exe,
  ; siehe Target WhisperNativeNichtBuendeln in LexWolf.csproj.
  SetOutPath "$INSTDIR\runtimes\win-x64"
  File "\build\lexwolf-desktop\runtimes\win-x64\*.dll"
  SetOutPath "$INSTDIR"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\LexWolf"
  CreateShortcut "$SMPROGRAMS\LexWolf\LexWolf.lnk" "$INSTDIR\LexWolf.exe"
  CreateShortcut "$SMPROGRAMS\LexWolf\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\LexWolf.lnk" "$INSTDIR\LexWolf.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\LexWolf" "DisplayName" "LexWolf"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\LexWolf" "UninstallString" "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\LexWolf" "DisplayVersion" "${VERSION}"

  ; Nach (Silent-)Install automatisch starten — sowohl für den Update-Fall
  ; als auch bei Erstinstallation praktisch.
  Exec '"$INSTDIR\LexWolf.exe"'
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\LexWolf.exe"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR\runtimes"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\LexWolf\LexWolf.lnk"
  Delete "$SMPROGRAMS\LexWolf\Uninstall.lnk"
  RMDir "$SMPROGRAMS\LexWolf"
  Delete "$DESKTOP\LexWolf.lnk"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\LexWolf"
SectionEnd
