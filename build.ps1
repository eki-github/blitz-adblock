$ErrorActionPreference = 'Stop'
$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (!(Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$out = Join-Path $PSScriptRoot 'dist'
[void][IO.Directory]::CreateDirectory($out)
$source = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$payload = Join-Path $PSScriptRoot 'src\filter.css'
$integration = Join-Path $PSScriptRoot 'integrate.ps1'
$resources = @('uninstall.ps1','files.ps1') | ForEach-Object { '/resource:' + (Join-Path $PSScriptRoot $_) + ',' + $_ }
& $compiler /nologo /optimize+ /target:exe /platform:anycpu /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll $resources "/resource:$integration,integrate.ps1" "/resource:$payload,filter.css" "/out:$out\BlitzAdblock.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output (Join-Path $out 'BlitzAdblock.exe')
