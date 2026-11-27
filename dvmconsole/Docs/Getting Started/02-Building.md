# Building

This page explains how to build the Digital Voice Modem Desktop Dispatch Console from source.

Most developers should use Visual Studio 2026 with the .NET desktop workload.

---

# Requirements

## Visual Studio

Install Visual Studio 2026 with:

```
.NET Desktop Development
```

## .NET SDK

The console project targets:

```
net10.0-windows7.0
```

Use the .NET 10 SDK or newer with Windows desktop support.

## Git

Git is required to clone the repository and submodules.

## Windows

The console is a WPF application and is intended to build and run on Windows 10 or newer.

## dvmvocoder

The console requires `libvocoder.DLL` from:

```
https://github.com/DVMProject/dvmvocoder
```

`libvocoder.DLL` must be present next to the built console executable. If it is missing, the console will stop at startup and display an error.

---

# Clone the Repository

Use `--recursive` so required submodules are downloaded.

```powershell
git clone --recursive https://github.com/DVMProject/dvmconsole.git
cd dvmconsole
```

If the repository was already cloned without submodules, run:

```powershell
git submodule update --init --recursive
```

---

# Open the Solution

Open:

```
dvmconsole.sln
```

from Visual Studio 2026.

You can open it by double-clicking the solution file or by using:

```
File > Open > Project/Solution
```

---

# Build

Use the included solution configuration. The console project is configured for `x86`.

Build with:

```
Build > Build Solution
```

or press:

```
Ctrl + Shift + B
```

The app includes WPF UI resources, audio assets, markdown documentation files, and the `fnecore` submodule.

If building for a different CPU architecture, `libvocoder.DLL` must be built for that same architecture.

---

# Build From PowerShell

From the repository root:

```powershell
dotnet build .\dvmconsole.sln
```

---

# Run

Run from Visual Studio with:

```
Debug > Start Debugging
```

or press:

```
F5
```

The compiled app is written under the project `bin` directory for the selected platform and configuration.

Example:

```
dvmconsole\bin\x86\Debug\net10.0-windows7.0\
```

---

# Release ZIP

## Download a Prebuilt Package

Open [GitHub Releases](https://github.com/DVMProject/dvmconsole/releases)
and choose the stable **Latest** release, when available, or a dev **Pre-release**
for testing. Under **Assets**, download
`DVMConsole-R02A00-win-x86.zip` or `DVMConsole-R02A00-win-x64.zip`, then extract the complete
ZIP. No GitHub account is required. The native vocoder is already included.

Checksums, the exact build revisions, and the pinned native vocoder source are
also attached to each release.

Install the matching .NET 10 Desktop Runtime as described below. No source build
is required to use these downloads. ARM64-native packages are not produced.

## Build a Package Locally

Do not distribute a working `bin` folder. It may contain private configurations,
keys, saved settings, recordings, or logs from local testing.

From the repository root, use a directory containing the matching native library:

```powershell
.\scripts\Publish-Release.ps1 -VocoderSourceDirectory 'C:\Build\vocoder\x86'
```

This creates a new directory under `artifacts\packages`, publishes a Release x86
build, checks required files and native architecture, and creates a ZIP plus a
SHA256 checksum. It includes the runtime manifests, dependency DLLs, documentation,
project notices, and example configurations. Unexpected files stop packaging.
Only reviewed example configurations belong in the release; never put real
credentials in them. The script does not sign or upload the package.

Use `-Architecture x64` only with an x64 native library and after testing that
package. `-OutputDirectory` can select a different **new** output directory;
existing directories are deliberately rejected to prevent stale files leaking
into a release. Do not run Console inside the publish folder before distribution.

Recipients must extract the **entire ZIP**, retaining its directory structure,
and install the **.NET 10 Desktop Runtime** matching the package architecture.
The default x86 package needs the x86 Desktop Runtime even on 64-bit Windows.
The ASP.NET Core Runtime or base .NET Runtime alone is not sufficient. Do not
send only the EXE/DLL or omit `.deps.json` and `.runtimeconfig.json`.

Before release, test a separate extracted copy on a clean Windows machine with
the matching Desktop Runtime: startup, built-in help, local audio, and a test
connection. A successful build on a developer PC does not verify prerequisites
on another PC. Keep test profiles and recordings outside the distribution copy.

About and startup logs show the UTC build timestamp embedded in the assembly,
not the file's modification date. Build automation can set `BuildTimestampUtc`
to an ISO 8601 UTC timestamp when it needs a fixed value.

---

# Documentation Files

The built-in documentation viewer reads markdown files from:

```
dvmconsole\Docs
```

The project file copies these docs into the build output. If new markdown files are added, make sure they are included as content in the project file so they appear in the in-app Documentation window.

---

# Troubleshooting

## Submodules are missing

Run:

```powershell
git submodule update --init --recursive
```

## Build fails due to missing Windows desktop support

Verify that Visual Studio has the `.NET Desktop Development` workload installed.

## Build succeeds but docs are missing in the app

Verify that the markdown files are included as content in `dvmconsole.csproj` and copied to the output directory.

## App stops at startup with a missing vocoder message

Verify that `libvocoder.DLL` is next to the built console executable and matches the selected CPU architecture.
