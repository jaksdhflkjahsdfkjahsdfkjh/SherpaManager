; Inno Setup script for Sherpa Manager.
;
; Packages the self-contained x64 publish output, so the installed application
; does not require a separately installed .NET runtime.
;
; Built by .github/workflows/release.yml. To build it by hand:
;
;   dotnet publish src/SherpaManager.csproj -c Release -r win-x64 ^
;     --self-contained true -o build/publish/self-contained
;   ISCC.exe /DAppVersion=0.4.5 installer\SherpaManager.iss
;
; The installer is written to build\artifacts\.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

#define AppName "Sherpa Manager"
#define AppExeName "SherpaManager.exe"
#define AppPublisher "Sherpa Manager contributors"
#define AppURL "https://github.com/jaksdhflkjahsdfkjahsdfkjh/SherpaManager"

[Setup]
; Never change AppId. It is how Windows recognises an existing installation
; and offers an upgrade instead of a second parallel copy.
AppId={{C6FE855E-2DB5-4B32-902B-82E4366BA7A5}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
VersionInfoVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}/issues
AppUpdatesURL={#AppURL}/releases

; Sherpa Manager binds nvapi64.dll and is x64 only.
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Install per-user by default so no UAC prompt is needed. The user can still
; choose an all-users install from the first page of the wizard.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

LicenseFile=..\LICENSE
SetupIconFile=..\src\Assets\SherpaManager.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

OutputDir=..\build\artifacts
OutputBaseFilename=SherpaManager-v{#AppVersion}-win-x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

; Offer to close a running instance rather than failing on a locked file.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\build\publish\self-contained\*"; DestDir: "{app}"; \
  Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
  Flags: nowait postinstall skipifsilent

; Profiles, display snapshots, and diagnostic logs live under %APPDATA%\SherpaManager
; and %LOCALAPPDATA%\SherpaManager. They are deliberately left in place on
; uninstall so that reinstalling does not discard a user's captured layouts.
