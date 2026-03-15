# Building

This section explains how to build the **Digital Voice Modem Desktop Dispatch Console** from source.

The console is built using **Visual Studio** and the provided solution file.

Most users only need a standard Visual Studio installation with the .NET desktop development workload installed.

---

# Requirements

Before building the console, make sure the following tools are installed.

## Visual Studio

Install **Visual Studio 2022** (or newer) with the following workload:

```
.NET Desktop Development
```

Download:

https://visualstudio.microsoft.com/

---

## Git

Git is required to clone the repository and its submodules.

Download:

https://git-scm.com/

---

# Dependencies

The console depends on the **dvmvocoder** library.

Repository:

https://github.com/DVMProject/dvmvocoder

When cloning the console repository using `--recurse-submodules`, this dependency will be downloaded automatically.

---

# Build Instructions

## 1. Clone the Repository

Open a terminal or command prompt and run:

```bash
git clone --recurse-submodules https://github.com/DVMProject/dvmconsole.git
```

This downloads the console source code along with required submodules.

---

## 2. Enter the Project Directory

```
cd dvmconsole
```

---

## 3. Open the Solution

Open the solution file in Visual Studio:

```
dvmconsole.sln
```

You can either:

- Double-click the `.sln` file  
- Or open it through **File > Open > Project/Solution** inside Visual Studio.

---

## 4. Select Build Architecture

In the Visual Studio toolbar, select:

```
x86
```

as the build architecture.

This is the recommended default.

---

## 5. Build the Project

Press:

```
Ctrl + Shift + B
```

or select:

```
Build > Build Solution
```

Visual Studio will compile the console.

---

# Running the Console

Once the build completes successfully, you can run the console directly from Visual Studio:

```
Debug > Start Debugging
```

or press:

```
F5
```

The compiled executable will also be located in:

```
dvmconsole\bin\x86\Debug\
```

or

```
dvmconsole\bin\x86\Release\
```

depending on your build configuration.

---

# x64 Builds

While x64 builds are supported, the **dvmvocoder** library must also be compiled for the x64 architecture.

If you plan to build the console for x64:

1. Build `dvmvocoder` for x64
2. Update the console project to reference the x64 library
3. Switch the solution platform to `x64`

For most users, the default **x86 build is recommended**.

---

# Troubleshooting

## Submodules did not download

If you cloned the repository without submodules, run:

```bash
git submodule update --init --recursive
```

## Build fails due to missing dependencies

Verify that:

- Visual Studio has the **.NET Desktop Development** workload installed
- The repository was cloned with `--recurse-submodules`