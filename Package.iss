; -- 64Bit.iss --
; Demonstrates installation of a program built for the x64 (a.k.a. AMD64)
; architecture.
; To successfully run this installation and the program it installs,
; you must have a "x64" edition of Windows.

; SEE THE DOCUMENTATION FOR DETAILS ON CREATING .ISS SCRIPT FILES!
 #define AppVersion "v1.4.6"

[Setup]
AppName=merz
AppVersion={#AppVersion}
WizardStyle=modern
PrivilegesRequired=lowest
DefaultDirName={userpf}\merz\{#AppVersion}
UsePreviousAppDir=no
DefaultGroupName=merz
UninstallDisplayIcon={app}\merz.dll
Compression=lzma2
SolidCompression=yes
OutputDir=target/packaged
OutputBaseFilename=merz-installer
; "ArchitecturesAllowed=x64" specifies that Setup cannot run on
; anything but x64.
ArchitecturesAllowed=x64
; "ArchitecturesInstallIn64BitMode=x64" requests that the install be
; done in "64-bit mode" on x64, meaning it should use the native
; 64-bit Program Files directory and the 64-bit view of the registry.
ArchitecturesInstallIn64BitMode=x64
LicenseFile=LICENCE.txt

#define BuildDir "merz_src/bin/Debug/net8.0-windows"

[Files]
; .NET 8 build: merz is a managed merz.dll that Spry loads in-process (MERZ.cs -> ExecuteAssembly).
; No merz.exe / merz.exe.config, and no System.Memory/Buffers/Numerics.Vectors (they are part of .NET 8).
Source: "{#BuildDir}/merz.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}/merz.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}/merz.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}/Scripts.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}/Newtonsoft.Json.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#BuildDir}/merz-config.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "merz_src/MERZ.cs"; DestDir: "{app}";
; Docs webpage
Source: "pm-spry-tools/docs/*"; DestDir: "{app}/docs/"; Flags: recursesubdirs
; Bundled materials
Source: "pm-spry-tools/bundled/*"; DestDir: "{app}/bundled/"; Flags: recursesubdirs
; Runtime Viewer
Source: "pm-spry-tools/spry-runtime-viewer/*"; DestDir: "{app}/spry-runtime-viewer/"; Flags: recursesubdirs
; License
Source: "pm-spry-tools/LICENCE.txt*"; DestDir: "{app}";

[InstallDelete]
; Remove .NET Framework leftovers if installing over an old build in the same folder.
Type: files; Name: "{app}\merz.exe"
Type: files; Name: "{app}\merz.exe.config"
Type: files; Name: "{app}\System.Memory.dll"
Type: files; Name: "{app}\System.Buffers.dll"
Type: files; Name: "{app}\System.Numerics.Vectors.dll"

[Run]
Filename: "{app}";Flags: shellexec




