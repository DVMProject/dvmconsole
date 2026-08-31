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
