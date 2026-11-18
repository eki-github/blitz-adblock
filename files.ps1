# Remove only hash-named patch builds in the two installation layouts.
function Remove-PatchVersions([string]$Folder, [string]$Keep, [switch]$RequireClosed) {
    $root = [IO.Path]::GetFullPath($Folder)
    $legacy = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'BlitzAdblock'))
    if ($root -ne $legacy -and (Split-Path $root -Leaf) -ne 'BlitzAdblockPatch') { throw 'Unexpected patch directory.' }
    $versions = Join-Path $root 'versions'
    if (![IO.Directory]::Exists($versions)) { return }
    foreach ($dir in @($root,$versions)) {
        if ((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Redirected patch directory.' }
    }
    foreach ($dir in Get-ChildItem -LiteralPath $versions -Directory -Force) {
        if ($dir.Name -notmatch '^[0-9a-f]{64}$' -or $dir.FullName -eq $Keep -or
            ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        $exe = Join-Path $dir.FullName 'BlitzAdblock.exe'
        if (![IO.File]::Exists($exe) -or ((Get-Item -LiteralPath $exe).Attributes -band [IO.FileAttributes]::ReparsePoint)) { continue }
        $stream = [IO.File]::OpenRead($exe)
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $digest = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($digest -ne $dir.Name) { continue }
        try { [IO.File]::Delete($exe) } catch {
            if ($RequireClosed) { throw "Exit Blitz from its tray menu and retry. The patch file is in use: $exe" }
            continue
        }
        foreach ($name in @('integrate.ps1','files.ps1','uninstall.ps1')) { [IO.File]::Delete((Join-Path $dir.FullName $name)) }
        if (@(Get-ChildItem -LiteralPath $dir.FullName -Force).Count -eq 0) { [IO.Directory]::Delete($dir.FullName) }
    }
    if (@(Get-ChildItem -LiteralPath $versions -Force).Count -eq 0) { [IO.Directory]::Delete($versions) }
    if (@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0) { [IO.Directory]::Delete($root) }
}
