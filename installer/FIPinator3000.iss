; FIPinator 3000 - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
; https://github.com/Pribilof-Hangar/fipinator-3000
; FIPinator 3000 (the gauge maker) installer (Inno Setup 6). Optional: FIPinator Live doesn't need it.
; Built by tools\build-release.ps1 from publish\release\FIPinator3000. Per-user, no admin rights.

#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#define AppName "FIPinator 3000"
#define AppExe "FIPinator3000.exe"

[Setup]
AppId={{6E2C9A41-5B7D-4E8F-9A30-F1D2C3B4A502}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Pribilof-Hangar
AppPublisherURL=https://github.com/Pribilof-Hangar/fipinator-3000
AppSupportURL=https://github.com/Pribilof-Hangar/fipinator-3000
DefaultDirName={localappdata}\Programs\{#AppName}
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installers
OutputBaseFilename=FIPinator-3000-Setup-{#AppVersion}
SetupIconFile=..\assets\icon\fipinator-3000.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
InfoBeforeFile=3000-before.txt
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Put a FIPinator 3000 shortcut on the desktop"

[InstallDelete]
; The bundled templates come with the program: clear the old copy so gauges dropped in a new version don't linger.
; (The user's own panels and gauges live in Documents\FIPinator 3000 and are never touched.)
Type: filesandordirs; Name: "{app}\Templates"

[Files]
Source: "..\publish\release\FIPinator3000\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Make and tweak FIP gauges"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Comment: "Make and tweak FIP gauges"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start FIPinator 3000 now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Only the program's own folder. Gauges you made (Documents\FIPinator 3000\Gauges) stay.
Type: filesandordirs; Name: "{app}"

[Messages]
FinishedLabel=FIPinator 3000 is installed.%n%nStart it from the desktop or the Start menu. The gauges you make are saved in Documents\FIPinator 3000\Gauges, where FIPinator Live finds them. Uninstalling never deletes them.

[Code]
function InitializeSetup(): Boolean;
var
  Version: TWindowsVersion;
begin
  Result := True;
  GetWindowsVersionEx(Version);
  if (Version.Major < 10) or ((Version.Major = 10) and (Version.Build < 22000)) then
  begin
    MsgBox('FIPinator 3000 is made for Windows 11, and this PC runs an older Windows.' + #13#10 + #13#10 +
      'It has only been tested on Windows 11, so it won''t install here.', mbError, MB_OK);
    Result := False;
  end;
end;
