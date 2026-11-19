# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) DVMProject Authors

<#
.SYNOPSIS
Builds a framework-dependent Windows release ZIP in a new directory.
.DESCRIPTION
Requires the .NET 10 SDK, initialized submodules, and a matching libvocoder.dll.
Never package an existing runtime directory or start Console from the package
directory before distributing it. Use a separate extracted copy for testing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$VocoderSourceDirectory,
    [ValidateSet('x86', 'x64')]
    [string]$Architecture = 'x86',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$native = (Resolve-Path -LiteralPath $VocoderSourceDirectory).Path
if (-not (Test-Path -LiteralPath (Join-Path $native 'libvocoder.dll') -PathType Leaf)) {
    throw 'VocoderSourceDirectory must contain libvocoder.dll.'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repo ('artifacts\packages\' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
}
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) {
    throw "OutputDirectory already exists. Choose a new directory: $output"
}
$null = New-Item -ItemType Directory -Path $output
$publish = Join-Path $output 'publish'
$build = Join-Path $output 'build'

& dotnet publish (Join-Path $repo 'dvmconsole\DVMConsole.csproj') `
    -c Release "-p:Platform=$Architecture" "-p:PlatformTarget=$Architecture" `
    '-p:SelfContained=false' '-p:PublishSingleFile=false' '-p:PublishTrimmed=false' `
    "-p:OutputPath=$build" "-p:PublishDir=$publish" "-p:VocoderSourceDirectory=$native\." `
    --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Release publish failed; no ZIP was created.' }

# Runtime DLLs come only from the dependency manifest and the required native
# library. Other content must match a named source file, never a runtime profile.
$required = @('DVMConsole.exe', 'DVMConsole.deps.json', 'DVMConsole.runtimeconfig.json', 'libvocoder.dll')
$deps = Get-Content -LiteralPath (Join-Path $publish 'DVMConsole.deps.json') -Raw | ConvertFrom-Json
foreach ($target in $deps.targets.PSObject.Properties.Value) {
    foreach ($library in $target.PSObject.Properties.Value) {
        foreach ($runtime in $library.runtime.PSObject.Properties) {
            $required += [IO.Path]::GetFileName($runtime.Name)
        }
    }
}
$sources = @{
    'README.md' = 'README.md'
    'LICENSE' = 'LICENSE'
    'AUTHORS.md' = 'AUTHORS.md'
    'licenses\FNECore-LICENSE' = 'fnecore\LICENSE'
    'licenses\FNECore-AUTHORS.md' = 'fnecore\AUTHORS.md'
    'configs\codeplug.example.yml' = 'configs\codeplug.example.yml'
    'configs\alias.example.yml' = 'configs\alias.example.yml'
}
$docs = & git -C $repo ls-files -- 'dvmconsole/Docs/*.md'
if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked documentation.' }
foreach ($doc in $docs) {
    $sources[$doc.Substring('dvmconsole/'.Length).Replace('/', '\')] = $doc
}
foreach ($tone in @('alert1', 'alert2', 'alert3', 'emergency', 'hold')) {
    $sources["Audio\$tone.wav"] = "dvmconsole\Audio\$tone.wav"
}
$required += @($sources.Keys)
$required = @($required | Sort-Object -Unique)
$allowed = $required + @('DVMConsole.pdb', 'fnecore.pdb')
$files = @(Get-ChildItem -LiteralPath $publish -Recurse -File -Force)
foreach ($file in $files) {
    $relative = $file.FullName.Substring($publish.Length + 1)
    if ($relative -notin $allowed) {
        throw "Unexpected release content: $relative. No ZIP was created."
    }
}
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish $relative) -PathType Leaf)) {
        throw "Required release file missing: $relative"
    }
}
foreach ($relative in $sources.Keys) {
    $sourceHash = (Get-FileHash -LiteralPath (Join-Path $repo $sources[$relative]) -Algorithm SHA256).Hash
    $publishHash = (Get-FileHash -LiteralPath (Join-Path $publish $relative) -Algorithm SHA256).Hash
    if ($sourceHash -ne $publishHash) { throw "Published content differs from source: $relative" }
}

$runtime = Get-Content -LiteralPath (Join-Path $publish 'DVMConsole.runtimeconfig.json') -Raw | ConvertFrom-Json
if ('Microsoft.WindowsDesktop.App' -notin @($runtime.runtimeOptions.frameworks.name)) {
    throw 'Release must declare its Windows Desktop Runtime dependency.'
}
$reader = [IO.BinaryReader]::new([IO.File]::OpenRead((Join-Path $publish 'DVMConsole.exe')))
try {
    $reader.BaseStream.Position = 0x3c
    $reader.BaseStream.Position = $reader.ReadInt32()
    if ($reader.ReadUInt32() -ne 0x4550) { throw 'Invalid Console apphost PE header.' }
    $expectedMachine = if ($Architecture -eq 'x86') { 0x14c } else { 0x8664 }
    if ($reader.ReadUInt16() -ne $expectedMachine) { throw 'Console apphost architecture mismatch.' }
}
finally { $reader.Dispose() }

# Repository-relative links have a different base in the extracted package.
$readmePath = Join-Path $publish 'README.md'
$readme = [IO.File]::ReadAllText($readmePath).Replace('(dvmconsole/Docs/', '(Docs/')
$readme = $readme.Replace('(./repo/Screenshot-3.png)', '(https://github.com/DVMProject/dvmconsole/raw/HEAD/repo/Screenshot-3.png)')
[IO.File]::WriteAllText($readmePath, $readme, [Text.UTF8Encoding]::new($false))

$hashes = @($files | Sort-Object FullName | ForEach-Object {
    [ordered]@{
        file = $_.FullName.Substring($publish.Length + 1)
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
})
$hashes | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'files.sha256.json') -Encoding UTF8
$zip = Join-Path $output "DVMConsole-win-$Architecture.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $zip)
$zipHash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$zipHash  $([IO.Path]::GetFileName($zip))" | Set-Content -LiteralPath "$zip.sha256" -Encoding ASCII
Write-Output "Release ZIP: $zip"
Write-Output "SHA256: $zipHash"
