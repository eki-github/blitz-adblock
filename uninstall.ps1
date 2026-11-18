$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$title = 'Uninstall Blitz Adblock Patch'
$answer = [Windows.Forms.MessageBox]::Show('Restore the original Blitz shortcuts and sign-in entry, and remove Blitz Adblock Patch?', $title, 'YesNo', 'Question', 'Button2')
if ($answer -ne 'Yes') { return }
try {
    $helper = Join-Path $PSScriptRoot 'BlitzAdblock.exe'
    & (Join-Path $PSScriptRoot 'integrate.ps1') -Action Restore -LauncherPath $helper
    [void][Windows.Forms.MessageBox]::Show('Blitz Adblock Patch has been removed.', $title)
} catch {
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, $title, 'OK', 'Error')
    exit 1
}
