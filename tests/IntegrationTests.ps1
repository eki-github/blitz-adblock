$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot
$base = Join-Path $repo 'work\tests'
[void][IO.Directory]::CreateDirectory($base)
$fixture = Join-Path $base ([guid]::NewGuid().ToString('N'))
$realLocal = $env:LOCALAPPDATA
try {
    $env:LOCALAPPDATA = Join-Path $fixture 'Local'
    $programs = Join-Path $fixture 'Start Menu'
    $desktop = Join-Path $fixture 'Desktop'
    $blitz = Join-Path $fixture 'Games\Blitz'
    $patch = Join-Path $blitz 'BlitzAdblockPatch'
    foreach ($dir in @($programs,$desktop,(Join-Path $blitz 'resources'))) { [void][IO.Directory]::CreateDirectory($dir) }
    $exe = Join-Path $blitz 'Blitz.exe'
    [IO.File]::WriteAllText($exe, 'fixture')
    [IO.File]::WriteAllText((Join-Path $blitz 'resources\app.asar'), 'fixture')
    $sh = New-Object -ComObject WScript.Shell
    $normal = Join-Path $programs 'Blitz.lnk'
    $link = $sh.CreateShortcut($normal)
    $link.TargetPath = $exe
    $link.Save()
    $original = (Get-FileHash -LiteralPath $normal).Hash
    $initialRun = '"'+$exe+'" --autostart'
    $script:mockRun = $initialRun
    # Isolate the Windows Run value while exercising real .lnk files.
    function Get-ItemProperty { return [pscustomobject]@{'com.blitz.app'=$script:mockRun} }
    function Set-ItemProperty { param($LiteralPath,$Name,$Value,$ErrorAction) $script:mockRun = $Value }
    . (Join-Path $repo 'integrate.ps1')
    $sample = Join-Path $repo 'dist\BlitzAdblock.exe'
    $version = Join-Path (Join-Path $patch 'versions') (Get-FileHash -LiteralPath $sample).Hash.ToLowerInvariant()
    [void][IO.Directory]::CreateDirectory($version)
    $helper = Join-Path $version 'BlitzAdblock.exe'
    Copy-Item -LiteralPath $sample -Destination $helper
    foreach ($name in @('integrate.ps1','uninstall.ps1','files.ps1')) { Copy-Item -LiteralPath (Join-Path $repo $name) -Destination $version }
    $uninstall = Join-Path $programs 'Uninstall Blitz Adblock Patch.lnk'
    foreach ($attempt in 1..2) { Invoke-PatchIntegration Apply $helper $programs $desktop }
    if ($sh.CreateShortcut($normal).TargetPath -ne $helper -or $script:mockRun -notlike '* startup --dir *') { throw 'Startup integration failed.' }
    if ($sh.CreateShortcut($uninstall).Arguments -ne ('-NoProfile -File "'+(Join-Path $version 'uninstall.ps1')+'"')) { throw 'Uninstall shortcut has an incorrect target.' }
    $foreign = Join-Path $patch 'keep-me.txt'
    [IO.File]::WriteAllText($foreign, 'user-owned')
    Invoke-PatchIntegration Restore $helper $programs $desktop
    if ((Get-FileHash -LiteralPath $normal).Hash -ne $original -or $script:mockRun -cne $initialRun) { throw 'Original entries were not restored.' }
    if ((Test-Path -LiteralPath $helper) -or (Test-Path -LiteralPath $uninstall) -or !(Test-Path -LiteralPath $foreign)) { throw 'Uninstall file ownership check failed.' }
    Write-Output 'Patch installation and uninstall tests passed.'
} finally {
    $env:LOCALAPPDATA = $realLocal
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ((Split-Path $resolved) -eq $base -and (Split-Path $resolved -Leaf) -match '^[0-9a-f]{32}$' -and
        (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
