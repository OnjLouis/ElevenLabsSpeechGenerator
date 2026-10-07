[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [string]$SigningKeyPath,
    [string]$CurlDistribution
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'src'
$portable = Join-Path $OutputRoot 'portable'
$executable = Join-Path $portable 'ElevenLabsSpeechGenerator.exe'
$archiveName = if ($CurlDistribution) { 'ElevenLabsSpeechGenerator-Win7.zip' } else { 'ElevenLabsSpeechGenerator.zip' }
$archive = Join-Path $OutputRoot $archiveName
$signature = $archive + '.sig'

if (Test-Path -LiteralPath $OutputRoot) {
    throw "OutputRoot must be a new, empty staging path: $OutputRoot"
}

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Could not find the .NET Framework C# compiler.'
}

New-Item -ItemType Directory -Path $portable | Out-Null
$sources = Get-ChildItem -LiteralPath $source -Filter '*.cs' | Sort-Object Name | ForEach-Object FullName
& $compiler /nologo /target:winexe /optimize+ "/out:$executable" "/reference:$root\Dependencies\NAudio.dll" /reference:System.dll /reference:System.Core.dll /reference:System.Security.dll /reference:System.Runtime.Serialization.dll /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll $sources
if ($LASTEXITCODE -ne 0) {
    throw 'Build failed.'
}

Copy-Item -LiteralPath (Join-Path $root 'Manual.html') -Destination $portable
Copy-Item -LiteralPath (Join-Path $root 'LICENSE.txt') -Destination $portable
Copy-Item -LiteralPath (Join-Path $root 'Dependencies\NAudio.dll'), (Join-Path $root 'ThirdPartyNotices.txt') -Destination $portable
if ($CurlDistribution) {
    $binary = Join-Path $CurlDistribution 'bin\curl.exe'
    $certificates = Join-Path $CurlDistribution 'bin\curl-ca-bundle.crt'
    $license = Join-Path $CurlDistribution 'COPYING.txt'
    $dependencies = Join-Path $CurlDistribution 'dep'
    foreach ($required in @($binary, $certificates, $license, $dependencies)) {
        if (-not (Test-Path -LiteralPath $required)) { throw "Incomplete curl distribution: $required" }
    }
    $transport = Join-Path $portable 'Win7Transport'
    New-Item -ItemType Directory -Path $transport | Out-Null
    Copy-Item -LiteralPath $binary, $certificates, $license -Destination $transport
    Copy-Item -LiteralPath $dependencies -Destination $transport -Recurse
}
Get-ChildItem -LiteralPath $portable | Compress-Archive -DestinationPath $archive -CompressionLevel Optimal

if ($SigningKeyPath) {
    if (-not (Test-Path -LiteralPath $SigningKeyPath -PathType Leaf)) {
        throw "Signing key not found: $SigningKeyPath"
    }
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider
    try {
        $rsa.FromXmlString([IO.File]::ReadAllText($SigningKeyPath))
        $bytes = [IO.File]::ReadAllBytes($archive)
        $hash = [Security.Cryptography.SHA256]::Create()
        try { $signed = $rsa.SignData($bytes, $hash) } finally { $hash.Dispose() }
        [IO.File]::WriteAllText($signature, [Convert]::ToBase64String($signed), (New-Object Text.UTF8Encoding($false)))
    }
    finally {
        $rsa.PersistKeyInCsp = $false
        $rsa.Dispose()
    }
}

Write-Host "Built $portable"
Write-Host "Packaged $archive"
if (Test-Path -LiteralPath $signature) { Write-Host "Signed $signature" }
