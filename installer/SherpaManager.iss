; Inno Setup script for Sherpa Manager.
;
; Packages the self-contained x64 publish output, so the installed application
; does not require a separately installed .NET runtime.
;
; Built by .github/workflows/release.yml, through scripts/New-ReleaseArtifacts.ps1,
; which passes the version from the release tag. To build it by hand, publish
; first and then compile, from the command line or the Inno Setup IDE:
;
;   dotnet publish src/SherpaManager.csproj -c Release -r win-x64 ^
;     --self-contained true -o build/publish/self-contained
;   ISCC.exe installer\SherpaManager.iss
;
; The installer is written to build\artifacts\.

#ifndef PublishDir
  #define PublishDir "..\build\publish\self-contained"
#endif
#ifndef ArtifactDir
  #define ArtifactDir "..\build\artifacts"
#endif

; Built by hand, nothing passes a version, so it is read from the binaries being
; packaged: an installer's version can then only ever be the version of what it
; installs. Those binaries must also match Directory.Build.props, because a
; publish left over from an earlier build would otherwise be packaged under its
; own older version without anyone noticing.
#ifndef AppVersion
  #if Copy(PublishDir, 2, 1) == ":" || Copy(PublishDir, 1, 2) == "\\"
    #define PublishedExe PublishDir + "\SherpaManager.exe"
  #else
    #define PublishedExe SourcePath + PublishDir + "\SherpaManager.exe"
  #endif
  #if !FileExists(PublishedExe)
    #error No published build to package. Run: dotnet publish src/SherpaManager.csproj -c Release -r win-x64 --self-contained true -o build/publish/self-contained
  #endif

  ; ProductVersion carries the commit after a "+", which is not part of the version.
  #define PublishedProductVersion GetStringFileInfo(PublishedExe, "ProductVersion")
  #define PlusAt Pos("+", PublishedProductVersion)
  #define AppVersion (PlusAt > 0 ? Copy(PublishedProductVersion, 1, PlusAt - 1) : PublishedProductVersion)

  ; Assigned with #expr rather than #define inside the sub: a #define there makes
  ; a variable local to that one call, so the version found was thrown away on
  ; return and the loop read to the end of the file without ever stopping.
  #define ProjectVersion ""
  #define PropsLine ""
  #define PropsHandle FileOpen(SourcePath + "..\Directory.Build.props")
  #sub ReadProjectVersion
    #expr PropsLine = FileRead(PropsHandle)
    #if Pos("<Version>", PropsLine) > 0
      #expr ProjectVersion = Copy(PropsLine, Pos("<Version>", PropsLine) + 9, Pos("</Version>", PropsLine) - Pos("<Version>", PropsLine) - 9)
    #endif
  #endsub
  #if PropsHandle
    #for {0; !FileEof(PropsHandle) && ProjectVersion == ""; 0} ReadProjectVersion
    #expr FileClose(PropsHandle)
  #endif

  #pragma message "Published build: " + AppVersion + "   Directory.Build.props: " + ProjectVersion
  #if ProjectVersion == ""
    #error Could not read <Version> from Directory.Build.props, so the published build cannot be checked.
  #endif
  #if ProjectVersion != AppVersion
    #error The published build is not the version in Directory.Build.props (see the compiler output for both). Publish again before packaging.
  #endif
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
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os

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

OutputDir={#ArtifactDir}
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
Source: "{#PublishDir}\*"; DestDir: "{app}"; \
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

[Code]
{ True when a Run command starts the given quoted executable, with or without
  arguments after it. Sherpa adds --minimized when it is set to start minimized,
  so an exact comparison would leave that entry behind on uninstall. Written
  without relying on short-circuit evaluation, so the character after the path
  is only read when there is one. }
function IsStartupCommandFor(Command, QuotedExecutable: String): Boolean;
var
  Trimmed: String;
begin
  Result := False;
  Trimmed := Trim(Command);
  if CompareText(Copy(Trimmed, 1, Length(QuotedExecutable)), QuotedExecutable) <> 0 then
    Exit;
  if Length(Trimmed) = Length(QuotedExecutable) then
    Result := True
  else
    Result := Trimmed[Length(QuotedExecutable) + 1] = ' ';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  StartupCommand: String;
  InstalledCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    InstalledCommand := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      'SherpaManager', StartupCommand) then
    begin
      { Leave an entry for a different installed or portable copy untouched. }
      if IsStartupCommandFor(StartupCommand, InstalledCommand) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'SherpaManager');
    end;
  end;
end;
