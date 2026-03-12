## Building

This project utilizes a standard Visual Studio solution for its build system.

The DDC software requires the library dependencies listed below. Generally, the software attempts to be as portable as possible and as library-free as possible. A basic Visual Studio install, with .NET is usually all that is needed to compile.

### Dependencies

- dvmvocoder (libvocoder); https://github.com/DVMProject/dvmvocoder

### Build Instructions

1. Clone the repository. `git clone --recurse-submodules https://github.com/DVMProject/dvmconsole.git`
2. Switch into the "dvmconsole" folder.
3. Open the "dvmconsole.sln" with Visual Studio.
4. Select "x86" as the CPU type.
5. Compile.

Please note that while x64 CPU types are supported, the dvmvocoder library must be compiled separately for that architecture.