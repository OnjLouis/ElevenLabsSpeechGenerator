[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$WorkRoot
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (Test-Path -LiteralPath $WorkRoot) {
    throw "WorkRoot must be a new, empty test path: $WorkRoot"
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Could not find the .NET Framework C# compiler.'
}

New-Item -ItemType Directory -Path $WorkRoot | Out-Null
$testExe = Join-Path $WorkRoot 'ElevenLabsSpeechGenerator.Tests.exe'
$sources = @()
$sources += Get-ChildItem -LiteralPath (Join-Path $root 'src') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
$sources += Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
$audio = Join-Path $root 'Dependencies\NAudio.dll'
& $compiler /nologo /target:exe /optimize+ /define:PRIVATE_TEST /main:ElevenLabsSpeechGenerator.SpeechTests "/out:$testExe" "/reference:$audio" /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) {
    throw 'Test harness compilation failed.'
}

Copy-Item -LiteralPath $audio -Destination $WorkRoot
& $testExe
if ($LASTEXITCODE -ne 0) {
    throw 'One or more tests failed.'
}
