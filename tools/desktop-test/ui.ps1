# Native window/dialog automation for the test app on the hidden desktop.
#   ui.ps1 list                 windows, plus each dialog's text and buttons
#   ui.ps1 tree                 every element in each dialog (for debugging selectors)
#   ui.ps1 click <name>         click a dialog button by its label (OK, Retry, Close, Cancel, ...)
#   ui.ps1 waitdialog <sec>     poll until a dialog appears, then list it
# UI Automation only sees its own desktop, so this re-runs itself on "CalcpadTest" via deskrun.
param([string]$Action = 'list', [string]$Arg1)
$Here = $PSScriptRoot
$Work = Join-Path $Here '.work'

if ($env:CALCPAD_UI_INNER -ne '1') {
    $out = Join-Path $Work 'ui-out.txt'
    $wait = if ($Action -eq 'waitdialog') { [int]$Arg1 + 30 } else { 60 }
    $env:CALCPAD_UI_INNER = '1'
    & (Join-Path $Work 'bin\deskrun\deskrun.exe') CalcpadTest $wait --log $out -- `
        powershell -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath $Action $Arg1 | Out-Null
    $code = $LASTEXITCODE
    $env:CALCPAD_UI_INNER = $null
    Get-Content $out -Encoding UTF8
    exit $code
}

[Console]::OutputEncoding = [Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type -Namespace W -Name U -MemberDefinition '[DllImport("user32.dll")] public static extern IntPtr PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);'
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$CT = [System.Windows.Automation.ControlType]
function Cond($prop, $value) { New-Object System.Windows.Automation.PropertyCondition($prop, $value) }

function Get-AppWindows {
    $pids = @(Get-Process calcpad-desktop -ErrorAction SilentlyContinue | ForEach-Object Id)
    $AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) |
        Where-Object { $pids -contains $_.Current.ProcessId }
}

# Native dialogs (message boxes, TaskDialogs, file pickers) use class #32770, top-level or under the main window.
function Get-Dialogs {
    foreach ($w in Get-AppWindows) {
        if ($w.Current.ClassName -eq '#32770') { $w }
        $w.FindAll($TS::Children, (Cond $AE::ClassNameProperty '#32770'))
    }
}

function Show-Dialogs {
    foreach ($w in Get-AppWindows) { "[window] '$($w.Current.Name)'" }
    foreach ($d in Get-Dialogs) {
        "--- DIALOG: '$($d.Current.Name)'"
        $d.FindAll($TS::Descendants, (Cond $AE::ControlTypeProperty $CT::Text)) | ForEach-Object { "  text: $($_.Current.Name)" }
        # TaskDialog buttons are CCPushButton panes rather than Buttons.
        $d.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
            Where-Object { $_.Current.ControlType -eq $CT::Button -or $_.Current.ClassName -eq 'CCPushButton' } |
            ForEach-Object { "  button: '$($_.Current.Name)'" }
    }
}

switch ($Action) {
    'list' { Show-Dialogs }
    'tree' {
        foreach ($d in Get-Dialogs) {
            "--- DIALOG '$($d.Current.Name)'"
            $d.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
                $c = $_.Current
                "  [$($c.ControlType.ProgrammaticName -replace 'ControlType.','')] '$($c.Name)' class=$($c.ClassName) hwnd=$($c.NativeWindowHandle) id=$($c.AutomationId)"
            }
        }
    }
    'click' {
        # BM_CLICK is posted, not sent: InvokePattern.Invoke can block on a closing modal.
        foreach ($d in Get-Dialogs) {
            $hit = $d.FindAll($TS::Descendants, (Cond $AE::NameProperty $Arg1)) |
                Where-Object { $_.Current.NativeWindowHandle -ne 0 } | Select-Object -First 1
            if ($hit) {
                [W.U]::PostMessage([IntPtr]$hit.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
                "clicked '$Arg1' in '$($d.Current.Name)'"
                exit 0
            }
        }
        "button '$Arg1' not found"
        exit 1
    }
    'waitdialog' {
        $t0 = Get-Date
        for ($i = 0; $i -lt [int]$Arg1; $i++) {
            if (@(Get-Dialogs).Count) { "dialog after $([int]((Get-Date) - $t0).TotalSeconds)s"; Show-Dialogs; exit 0 }
            Start-Sleep 1
        }
        "no dialog after $Arg1 s"
        Show-Dialogs
        exit 1
    }
    default { "unknown action '$Action'"; exit 2 }
}
