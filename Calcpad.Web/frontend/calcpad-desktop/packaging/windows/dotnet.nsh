!define CALCPAD_DOTNET_HOOK_DIR "${__FILEDIR__}"
!define MULTIUSER_INCLUDED
!macroundef MUI_PAGE_WELCOME
!macro MUI_PAGE_WELCOME
  !undef MUI_PAGE_CUSTOMFUNCTION_PRE
!macroend

!macro MULTIUSER_PAGE_INSTALLMODE
  !undef MUI_PAGE_CUSTOMFUNCTION_PRE
  !include "${CALCPAD_DOTNET_HOOK_DIR}\install-scope.nsh"
!macroend

!macro MULTIUSER_INIT
  Call Calcpad.InitInstallScope
!macroend

!macro MULTIUSER_UNINIT
  Call un.Calcpad.InitInstallScope
!macroend

!macro NSIS_HOOK_PREINSTALL
  InitPluginsDir
  File /oname=$PLUGINSDIR\install-dotnet.ps1 "${CALCPAD_DOTNET_HOOK_DIR}\install-dotnet.ps1"
  File /oname=$PLUGINSDIR\runtimeconfig.json "${CALCPAD_DOTNET_HOOK_DIR}\..\..\src-tauri\binaries\Calcpad.Server.runtimeconfig.json"
  DetailPrint "Checking .NET prerequisites..."
  nsExec::ExecToLog '"$SYSDIR\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File "$PLUGINSDIR\install-dotnet.ps1" -RuntimeConfig "$PLUGINSDIR\runtimeconfig.json" -Architecture "${ARCH}" -InstallMode "$MultiUser.InstallMode" -UserRuntimeRoot "$INSTDIR\.dotnet"'
  Pop $0
  ${If} $0 == 3010
    SetRebootFlag true
  ${ElseIf} $0 != 0
    MessageBox MB_OK|MB_ICONSTOP "The required .NET runtimes could not be installed. Check your internet connection and the installer details, then try again." /SD IDOK
    Abort
  ${EndIf}
!macroend

!macro NSIS_HOOK_POSTUNINSTALL
  RMDir /r "$INSTDIR\.dotnet"
  RMDir "$INSTDIR"
!macroend
