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
#define AppPublisher "Metis"
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
; The licence has to be accepted before anything is installed. A licence that
; only sits in the repository is a document; one the installer makes you agree
; to is the agreement.
LicenseFile=..\LICENSE
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
; Reproducing these notices is a condition of the MIT and Apache-2.0 licences
; Metis's dependencies are under, so they ship with the application rather than
; living only in the repository.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\PRIVACY.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "Learn the digital world by doing — Metis teaches while you work"; IconFilename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "Learn the digital world by doing — Metis teaches while you work"; IconFilename: "{app}\{#AppExeName}"

[Registry]
; Metis writes this itself when 'start Metis when I sign in' is ticked, so the
; installer must not create it -- but it must take it away again. Left behind,
; it relaunches an executable that is no longer installed, and on a reinstall
; it silently resurrects a preference the user was never asked about.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Metis"; ValueType: none; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
{ Uninstalling used to leave every trace of Metis behind: the settings file
  under %LOCALAPPDATA%\Metis, the diagnostic log, and the saved sign-in in
  Windows Credential Manager. None of that lives under {app}, so none of it was
  ever removed -- which meant a reinstall was not a fresh install. It came back
  already onboarded, already signed in as whoever used the machine last, and
  therefore never showed the first-run wizard at all.

  Deleting it silently would be worse than leaving it, because someone
  uninstalling to fix a problem usually wants their settings to survive. So the
  uninstaller asks, and defaults to keeping them. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
  ResultCode: Integer;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if MsgBox('Also remove your Metis settings, diagnostic log and saved sign-in?'
            + #13#10#13#10
            + 'Choose No to keep them, so reinstalling Metis picks up where you left off.',
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) <> IDYES then
    Exit;

  DataDir := ExpandConstant('{localappdata}\Metis');
  if DirExists(DataDir) then
    DelTree(DataDir, True, True, True);

  { The refresh token is in Credential Manager rather than in a file, so it
    outlives the folder above and would sign the next install in as this user. }
  Exec(ExpandConstant('{cmd}'), '/c cmdkey /delete:Metis/Supabase/RefreshToken',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;
