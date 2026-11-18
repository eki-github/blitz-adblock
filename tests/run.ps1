$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$out = Join-Path $root 'work\tests'
[void][IO.Directory]::CreateDirectory($out)
$sources = @(Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$test = Join-Path $PSScriptRoot 'FilterTests.cs'
& $compiler /nologo /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /main:BlitzAdblock.FilterTests "/out:$out\FilterTests.exe" $sources $test
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& (Join-Path $out 'FilterTests.exe')
if ($LASTEXITCODE -ne 0) { throw 'Filter tests failed.' }
& (Join-Path $PSScriptRoot 'IntegrationTests.ps1')
