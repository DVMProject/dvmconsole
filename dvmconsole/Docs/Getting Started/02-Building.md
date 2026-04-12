# Building

This page explains how to build the Digital Voice Modem Desktop Dispatch Console from source.

Most developers should use Visual Studio with the .NET desktop workload.

---

# Requirements

## Visual Studio

Install Visual Studio 2022 or newer with:

```
.NET Desktop Development
```

## Git

Git is required to clone the repository and submodules.

## Windows

The console is a WPF application and is intended to build and run on Windows.

---

# Clone the Repository

Use `--recurse-submodules` so required submodules are downloaded.

```bash
git clone --recurse-submodules https://github.com/DVMProject/dvmconsole.git
cd dvmconsole
```

If the repository was already cloned without submodules, run:

```bash
git submodule update --init --recursive
```

---

# Open the Solution

Open:

```
dvmconsole.sln
```

from Visual Studio.

You can open it by double-clicking the solution file or by using:

```
File > Open > Project/Solution
```

---

# Build

Select the desired platform, usually `x64` or `x86`, then build:

```
Build > Build Solution
```

or press:

```
Ctrl + Shift + B
```

The app targets .NET for Windows and includes WPF UI resources, audio assets, and markdown documentation files.

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
dvmconsole\bin\x64\Debug\net8.0-windows7.0\
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

```bash
git submodule update --init --recursive
```

## Build fails due to missing Windows desktop support

Verify that Visual Studio has the `.NET Desktop Development` workload installed.

## Build succeeds but docs are missing in the app

Verify that the markdown files are included as content in `dvmconsole.csproj` and copied to the output directory.
