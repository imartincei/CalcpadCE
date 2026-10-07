RequestExecutionLevel user

Var MultiUser.InstallMode
Var Calcpad.ScopeDialog
Var Calcpad.CurrentUserRadio
Var Calcpad.AllUsersRadio
Var Calcpad.ScopeParameters
Var Calcpad.ScopeResult
Var Calcpad.ElevatedRelaunch

Page custom Calcpad.InstallScopeCreate Calcpad.InstallScopeLeave

Function Calcpad.SetCurrentUser
  StrCpy $MultiUser.InstallMode "CurrentUser"
  SetShellVarContext current
  StrCpy $INSTDIR "$LOCALAPPDATA\Programs\${PRODUCTNAME}"
  Call RestorePreviousInstallLocation
FunctionEnd

Function Calcpad.SetAllUsers
  StrCpy $MultiUser.InstallMode "AllUsers"
  SetShellVarContext all
  StrCpy $INSTDIR "$PROGRAMFILES64\${PRODUCTNAME}"
  Call RestorePreviousInstallLocation
FunctionEnd

Function Calcpad.Elevate
  UserInfo::GetAccountType
  Pop $Calcpad.ScopeResult
  ${If} $Calcpad.ScopeResult != "Admin"
    ${GetParameters} $Calcpad.ScopeParameters
    ClearErrors
    ExecShellWait "runas" "$EXEPATH" '$Calcpad.ScopeParameters /AllUsers /CALCPAD_ELEVATED' SW_SHOWNORMAL $Calcpad.ScopeResult
    ${If} ${Errors}
      MessageBox MB_OK|MB_ICONEXCLAMATION "Administrator approval is required to install for everyone. You can choose Only for me to install without it." /SD IDOK
      Abort
    ${EndIf}
    SetErrorLevel $Calcpad.ScopeResult
    Quit
  ${EndIf}
FunctionEnd

Function Calcpad.InitInstallScope
  Call Calcpad.SetCurrentUser
  ${GetParameters} $Calcpad.ScopeParameters
  ClearErrors
  ${GetOptions} $Calcpad.ScopeParameters "/AllUsers" $Calcpad.ScopeResult
  ${IfNot} ${Errors}
    Call Calcpad.Elevate
    Call Calcpad.SetAllUsers
  ${EndIf}
  ClearErrors
  ${GetOptions} $Calcpad.ScopeParameters "/CALCPAD_ELEVATED" $Calcpad.ElevatedRelaunch
  ${IfNot} ${Errors}
    StrCpy $Calcpad.ElevatedRelaunch 1
  ${EndIf}
FunctionEnd

Function Calcpad.InstallScopeCreate
  Call SkipIfPassive
  ${If} $Calcpad.ElevatedRelaunch == 1
  ${AndIf} $MultiUser.InstallMode == "AllUsers"
    Abort
  ${EndIf}
  !insertmacro MUI_HEADER_TEXT "Choose who can use ${PRODUCTNAME}" "Install for yourself or for everyone on this computer."
  nsDialogs::Create 1018
  Pop $Calcpad.ScopeDialog
  ${NSD_CreateRadioButton} 15u 25u -15u 12u "Only for me"
  Pop $Calcpad.CurrentUserRadio
  ${NSD_CreateRadioButton} 15u 50u -15u 24u "Anyone who uses this computer (administrator approval required)"
  Pop $Calcpad.AllUsersRadio
  ${If} $MultiUser.InstallMode == "AllUsers"
    ${NSD_Check} $Calcpad.AllUsersRadio
  ${Else}
    ${NSD_Check} $Calcpad.CurrentUserRadio
  ${EndIf}
  nsDialogs::Show
FunctionEnd

Function Calcpad.InstallScopeLeave
  ${NSD_GetState} $Calcpad.AllUsersRadio $Calcpad.ScopeResult
  ${If} $Calcpad.ScopeResult == ${BST_CHECKED}
    Call Calcpad.Elevate
    Call Calcpad.SetAllUsers
  ${Else}
    Call Calcpad.SetCurrentUser
  ${EndIf}
FunctionEnd

Function un.Calcpad.InitInstallScope
  ReadRegStr $Calcpad.ScopeResult HKLM "${UNINSTKEY}" "UninstallString"
  ${If} $Calcpad.ScopeResult == '$\"$INSTDIR\uninstall.exe$\"'
    UserInfo::GetAccountType
    Pop $Calcpad.ScopeResult
    ${If} $Calcpad.ScopeResult != "Admin"
      ${GetParameters} $Calcpad.ScopeParameters
      ClearErrors
      ExecShellWait "runas" "$INSTDIR\uninstall.exe" '$Calcpad.ScopeParameters _?=$INSTDIR' SW_SHOWNORMAL $Calcpad.ScopeResult
      ${If} ${Errors}
        SetErrorLevel 1223
        Quit
      ${EndIf}
      SetErrorLevel $Calcpad.ScopeResult
      Quit
    ${EndIf}
    StrCpy $MultiUser.InstallMode "AllUsers"
    SetShellVarContext all
  ${Else}
    StrCpy $MultiUser.InstallMode "CurrentUser"
    SetShellVarContext current
  ${EndIf}
FunctionEnd
