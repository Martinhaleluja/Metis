#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\installer-publish"
#endif

#ifndef InstallerIcon
  #define InstallerIcon ".generated\Metis.ico"
#endif

#define AppName "Metis"
; Credited on the setup wizard, in Add/Remove Programs, and in the file
; properties of both the installer and the application.
#define AppPublisher "Martin Nakasole"
#define AppExeName "Metis.exe"
#define AppUrl "https://github.com/Martinhaleluja/Metis"

[Setup]
AppId={{A8139F5A-9D9F-45FA-B825-0CA65F70C75D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=output
OutputBaseFilename=Metis-Setup-{#AppVersion}-win-x64
SetupIconFile={#InstallerIcon}
UninstallDisplayIcon={app}\{#AppExeName}
; Shown before the install location page. Metis captures the whole desktop and
; sends it to a cloud provider by default, so that is disclosed before anything
; is written to disk rather than only inside the app's own first-run wizard.
InfoBeforeFile=Info.txt
AppComments=An AI companion for learning the digital world by doing
WizardImageAlphaFormat=defined
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=110
CloseApplications=force
RestartApplications=no
SetupLogging=yes
UsePreviousAppDir=yes
ChangesAssociations=no
ChangesEnvironment=no
VersionInfoVersion={#AppVersion}.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Metis — An AI Companion for Learning the Digital World by Doing
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCopyright=Copyright (c) 2026 {#AppPublisher}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "Learn the digital world by doing — Metis teaches while you work"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "Learn the digital world by doing — Metis teaches while you work"; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
