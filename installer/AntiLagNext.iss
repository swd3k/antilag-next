; AntiLag Next — Inno Setup script
; ISCC defines:
;   /DArch=x64|x86|arm64
;   /DSourceDir=...   (published UI folder, e.g. dist\AntiLagNext-win-x64)
;   /DOutName=...     (Setup.exe base name without extension)
;   /DMyAppVersion=1.0.0

#ifndef Arch
  #define Arch "x64"
#endif

#ifndef SourceDir
  #define SourceDir "..\dist\AntiLagNext-win-x64"
#endif

#ifndef OutName
  #define OutName "AntiLagNext-Setup-win-x64"
#endif

#ifndef MyAppVersion
  #define MyAppVersion "1.3.0"
#endif

#define MyAppName "AntiLag Next"
#define MyAppPublisher "swd3k"
#define MyAppAuthorURL "https://github.com/swd3k"
#define MyAppURL "https://github.com/swd3k/antilag-next"
#define MyAppExeName "AntiLagNext.exe"
#define MyAppCopyright "Copyright (c) 2026 swd3k"

#if Arch == "x86"
  #define MyArchLabel "32-bit (x86)"
  #define MyArchitecturesAllowed "x86compatible"
  #define MyArchitecturesInstallIn64BitMode ""
  #define MyAppId "{{B5C6D7E8-A1A1-4A2B-9C0D-E1F2A3B4C586}"
#elif Arch == "arm64"
  ; Inno Setup uses "arm64" (not arm64compatible) for Arm64 packages
  #define MyArchLabel "ARM64"
  #define MyArchitecturesAllowed "arm64"
  #define MyArchitecturesInstallIn64BitMode "arm64"
  #define MyAppId "{{B5C6D7E8-A1A1-4A2B-9C0D-E1F2A3B4C5A4}"
#else
  #define MyArchLabel "64-bit (x64)"
  #define MyArchitecturesAllowed "x64compatible"
  #define MyArchitecturesInstallIn64BitMode "x64compatible"
  #define MyAppId "{{B5C6D7E8-A1A1-4A2B-9C0D-E1F2A3B4C564}"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion} ({#MyArchLabel})
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppAuthorURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\AntiLagNext
DefaultGroupName={#MyAppName}
; Reinstall / silent update: keep previous folder, skip dir page, no "folder exists?" prompt
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
DisableDirPage=auto
DisableProgramGroupPage=yes
DirExistsWarning=no
LicenseFile=..\LICENSE
InfoBeforeFile=
OutputDir=..\dist\installers
OutputBaseFilename={#OutName}
SetupIconFile=..\AntiLagNext\src\AntiLagNext.Ui\Assets\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
MinVersion=10.0
#if MyArchitecturesInstallIn64BitMode != ""
ArchitecturesAllowed={#MyArchitecturesAllowed}
ArchitecturesInstallIn64BitMode={#MyArchitecturesInstallIn64BitMode}
#else
ArchitecturesAllowed={#MyArchitecturesAllowed}
#endif
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} ({#MyArchLabel})
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright={#MyAppCopyright}
VersionInfoDescription={#MyAppName} Setup ({#MyArchLabel}) by {#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}.0
VersionInfoTextVersion={#MyAppVersion}
; Close running app so files can be replaced (in-app updater + manual reinstall)
CloseApplications=yes
CloseApplicationsFilter=AntiLagNext.exe
RestartApplications=no
AllowNoIcons=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startupicon"; Description: "Start with Windows (Run registry)"; GroupDescription: "Autostart:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{group}\GitHub repository"; Filename: "{#MyAppURL}"
Name: "{group}\Author swd3k"; Filename: "{#MyAppAuthorURL}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AntiLagNext"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
