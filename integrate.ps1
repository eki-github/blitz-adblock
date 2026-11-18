param(
    [ValidateSet('Apply','Restore')] [string]$Action = 'Apply',
    [string]$LauncherPath = (Join-Path $PSScriptRoot 'dist\BlitzAdblock.exe')
)
function Invoke-PatchIntegration {
param(
    [string]$Action, [string]$LauncherPath,
    [string]$Programs = [Environment]::GetFolderPath('Programs'),
    [string]$Desktop = [Environment]::GetFolderPath('DesktopDirectory')
)
$ErrorActionPreference = 'Stop'
$store = Join-Path $env:LOCALAPPDATA 'BlitzAdblock\integration'
$stateFile = Join-Path $store 'entries.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runName = 'com.blitz.app'
function Set-BlitzStartup([string]$Expected, [string]$Next) {
    if ((Get-ItemProperty -LiteralPath $runKey).$runName -cne $Expected) { throw 'Blitz startup changed during integration.' }
    try { Set-ItemProperty -LiteralPath $runKey -Name $runName -Value $Next -ErrorAction Stop; return }
    catch [System.Security.SecurityException] {}
    catch [System.UnauthorizedAccessException] {}
    # Elevate only the single compare-and-set against the original user's hive.
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $payload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes((@{sid=$sid;expected=$Expected;next=$Next}|ConvertTo-Json)))
    $body = @'
$ErrorActionPreference='Stop'
$v=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('PAYLOAD'))|ConvertFrom-Json
$key='Registry::HKEY_USERS\'+$v.sid+'\Software\Microsoft\Windows\CurrentVersion\Run'
if ((Get-ItemProperty -LiteralPath $key).'com.blitz.app' -cne $v.expected) {exit 2}
Set-ItemProperty -LiteralPath $key -Name 'com.blitz.app' -Value $v.next
'@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($body.Replace('PAYLOAD',$payload)))
    $admin = Start-Process -FilePath (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-NonInteractive','-EncodedCommand',$encoded) -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    if ($admin.ExitCode -ne 0 -or (Get-ItemProperty -LiteralPath $runKey).$runName -cne $Next) {throw 'Windows startup entry was not updated.'}
}
$shell = New-Object -ComObject WScript.Shell
$settingsLink = Join-Path ($Programs) 'Blitz Adblock Settings.lnk'
$settingsDescription = 'Blitz Adblock integration settings'
$uninstallLink = Join-Path ($Programs) 'Uninstall Blitz Adblock Patch.lnk'
$uninstallDescription = 'Uninstall Blitz Adblock Patch'
$legacyLink = Join-Path ($Desktop) 'Blitz (Adblock).lnk'
$versionDir = Split-Path ([IO.Path]::GetFullPath($LauncherPath))
$installedLayout = (Split-Path $versionDir -Leaf) -match '^[a-f0-9]{64}$' -and
    (Split-Path (Split-Path $versionDir) -Leaf) -eq 'versions' -and
    (Split-Path (Split-Path (Split-Path $versionDir)) -Leaf) -eq 'BlitzAdblockPatch'
$patchFolder = Split-Path (Split-Path $versionDir)
. (Join-Path $PSScriptRoot 'files.ps1')
$lock = New-Object Threading.Mutex($false,'Local\BlitzAdblockIntegration')
try {
    try { $held = $lock.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
    if (!$held) { throw 'Another integration operation is active.' }
    $state = if (Test-Path -LiteralPath $stateFile) {
        Get-Content -Raw -Encoding UTF8 -LiteralPath $stateFile | ConvertFrom-Json
    } else { [pscustomobject]@{links=@(); run=$null} }
    $currentRun = (Get-ItemProperty -LiteralPath $runKey -Name $runName -ErrorAction SilentlyContinue).$runName
    if ($Action -eq 'Restore') {
        if ($installedLayout) {
            $active = @(Get-Process -Name BlitzAdblock -ErrorAction SilentlyContinue | Where-Object {
                $_.Path -and $_.Path.StartsWith($patchFolder+'\',[StringComparison]::OrdinalIgnoreCase)
            })
            if ($active.Count) { throw 'Exit Blitz from the system tray and run the uninstaller again.' }
        }
        foreach ($entry in $state.links) {
            if (!(Test-Path -LiteralPath $entry.path)) { continue }
            $link = $shell.CreateShortcut($entry.path)
            if (($link.TargetPath -in @($entry.helper,$entry.previousHelper)) -and $link.Arguments -ceq $entry.args) {
                $temp = Join-Path (Split-Path $entry.path) ([guid]::NewGuid().ToString()+'.tmp')
                try {
                    [IO.File]::WriteAllBytes($temp,[Convert]::FromBase64String($entry.original))
                    [IO.File]::Replace($temp,$entry.path,($temp+'.old'))
                } finally { [IO.File]::Delete($temp); [IO.File]::Delete($temp+'.old') }
            }
        }
        if ($state.run -and $currentRun -cin @($state.run.patched,$state.run.previousPatched)) {
            if ($currentRun) { Set-BlitzStartup $currentRun $state.run.original }
        }
        if ((Test-Path -LiteralPath $settingsLink) -and $shell.CreateShortcut($settingsLink).Description -eq $settingsDescription) {
            [IO.File]::Delete($settingsLink)
        }
        if ($installedLayout) {
            if (Test-Path -LiteralPath $legacyLink) {
                $old = $shell.CreateShortcut($legacyLink)
                if ($old.Description -eq 'Blitz Adblock desktop launcher' -and
                    $old.TargetPath.StartsWith($patchFolder+'\',[StringComparison]::OrdinalIgnoreCase)) { [IO.File]::Delete($legacyLink) }
            }
            Remove-PatchVersions -Folder (Split-Path $store) -RequireClosed
            Remove-PatchVersions -Folder $patchFolder -RequireClosed
            if ((Test-Path -LiteralPath $uninstallLink) -and $shell.CreateShortcut($uninstallLink).Description -eq $uninstallDescription) {
                [IO.File]::Delete($uninstallLink)
            }
        }
        if (Test-Path -LiteralPath $stateFile) { [IO.File]::Delete($stateFile) }
        foreach ($dir in @($store,(Split-Path $store))) {
            if ([IO.Directory]::Exists($dir) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) { [IO.Directory]::Delete($dir) }
        }
        Write-Output 'Restored matching Blitz entries.'
        return
    }
    $helper = (Resolve-Path -LiteralPath $LauncherPath).Path
    if ([IO.Path]::GetFileName($helper) -ne 'BlitzAdblock.exe') { throw 'Expected BlitzAdblock.exe.' }
    $folders = @($Desktop,$Programs)
    foreach ($folder in $folders) {
        foreach ($file in Get-ChildItem -LiteralPath $folder -Filter 'Blitz.lnk' -Recurse -File) {
            $link = $shell.CreateShortcut($file.FullName)
            $target = $link.TargetPath
            $record = @($state.links | Where-Object path -eq $file.FullName)
            if ($record.Count -and $target -in @($record[0].helper,$record[0].previousHelper) -and $link.Arguments -ceq $record[0].args) {
                $record[0] | Add-Member NoteProperty previousHelper $target -Force
                $record[0].helper = $helper
                continue
            }
            if ([IO.Path]::GetFileName($target) -ne 'Blitz.exe' -or $link.Arguments -or
                !(Test-Path -LiteralPath (Join-Path (Split-Path $target) 'resources\app.asar'))) { continue }
            $state.links = @($state.links | Where-Object path -ne $file.FullName) + @([pscustomobject]@{
                path=$file.FullName; helper=$helper; args=('launch --dir "'+(Split-Path $target)+'"')
                original=[Convert]::ToBase64String([IO.File]::ReadAllBytes($file.FullName)); target=$target
            })
        }
    }
    if ($currentRun -match '^"([^"]+\\Blitz\.exe)"\s+--autostart$') {
        $dir = Split-Path $Matches[1]
        if (Test-Path -LiteralPath (Join-Path $dir 'resources\app.asar')) {
            $state.run = [pscustomobject]@{original=$currentRun;patched=('"'+$helper+'" startup --dir "'+$dir+'"')}
        }
    } elseif ($currentRun -and $state.run -and $currentRun -cin @($state.run.patched,$state.run.previousPatched)) {
        if ($state.run.original -match '^"([^"]+\\Blitz\.exe)"\s+--autostart$') {
            $dir = Split-Path $Matches[1]
            $state.run | Add-Member NoteProperty previousPatched $currentRun -Force
            $state.run.patched = '"'+$helper+'" startup --dir "'+$dir+'"'
        }
    }
    if (!$state.links.Count -and !$state.run) { throw 'No supported existing Blitz startup entries found.' }
    [void][IO.Directory]::CreateDirectory($store)
    $temp = Join-Path $store ([guid]::NewGuid().ToString()+'.tmp')
    try {
        [IO.File]::WriteAllText($temp,($state | ConvertTo-Json -Depth 5))
        if ([IO.File]::Exists($stateFile)) { [IO.File]::Replace($temp,$stateFile,($temp+'.old')) }
        else { [IO.File]::Move($temp,$stateFile) }
    } finally { [IO.File]::Delete($temp); [IO.File]::Delete($temp+'.old') }
    foreach ($entry in $state.links) {
        if (!(Test-Path -LiteralPath $entry.path)) { continue }
        $link=$shell.CreateShortcut($entry.path)
        if (($link.TargetPath -eq $entry.target -and !$link.Arguments) -or
            ($entry.previousHelper -and $link.TargetPath -eq $entry.previousHelper -and $link.TargetPath -ne $helper -and $link.Arguments -ceq $entry.args)) {
            $link.TargetPath=$helper
            $link.Arguments=$entry.args
            $link.WindowStyle=7
            $link.Save()
        }
        if ($link.TargetPath -eq $helper -and $link.Arguments -ceq $entry.args) { Write-Output ('Shortcut: '+$entry.path) }
    }
    if ($state.run -and $currentRun -and $currentRun -cne $state.run.patched -and $currentRun -cin @($state.run.original,$state.run.previousPatched)) {
        Set-BlitzStartup $currentRun $state.run.patched
        Write-Output 'Integrated existing Blitz autostart.'
    }
    if ($installedLayout) {
        $link = $shell.CreateShortcut($uninstallLink)
        if ((Test-Path -LiteralPath $uninstallLink) -and $link.Description -ne $uninstallDescription) { throw 'Uninstall shortcut name is already in use.' }
        $link.TargetPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $link.Arguments = '-NoProfile -File "' + (Join-Path $versionDir 'uninstall.ps1') + '"'
        $link.IconLocation = $helper+',0'
        $link.Description = $uninstallDescription
        $link.WorkingDirectory = [Environment]::GetFolderPath('LocalApplicationData')
        $link.WindowStyle = 7
        $link.Save()
        if (Test-Path -LiteralPath $legacyLink) {
            $old = $shell.CreateShortcut($legacyLink)
            if ($old.Description -eq 'Blitz Adblock desktop launcher' -and
                $old.Arguments -eq ('launch --dir "'+(Split-Path $patchFolder)+'"')) {
                $old.TargetPath = $helper
                $old.Save()
            }
        }
        if ((Test-Path -LiteralPath $settingsLink) -and $shell.CreateShortcut($settingsLink).Description -eq $settingsDescription) {
            [IO.File]::Delete($settingsLink)
        }
        Remove-PatchVersions -Folder (Split-Path $store)
        Remove-PatchVersions -Folder $patchFolder -Keep $versionDir
    }
} finally {
    if ($held) { $lock.ReleaseMutex() }
    $lock.Dispose()
}

}
if ($MyInvocation.InvocationName -ne '.') { Invoke-PatchIntegration $Action $LauncherPath }
