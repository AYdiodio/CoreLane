[Setup]
AppId=CoreLane
AppName=CoreLane
AppVersion={#CoreLaneVersion}
AppPublisher=CoreLane
AppPublisherURL=https://github.com/AYdiodio/CoreLane
DefaultDirName={localappdata}\Programs\CoreLane
DefaultGroupName=CoreLane
DisableDirPage=no
DisableProgramGroupPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
Uninstallable=no
CloseApplications=yes
CloseApplicationsFilter=CoreLane.exe,CoreLane.exe.config
RestartApplications=no
SetupIconFile=..\assets\branding\blue-violet.ico
OutputBaseFilename=CoreLane-v{#CoreLaneVersion}-win-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"

[Files]
Source: "{#CoreLaneSource}"; DestDir: "{app}"; DestName: "CoreLane.exe"; Flags: ignoreversion
Source: "{#CoreLaneSource}.config"; DestDir: "{app}"; DestName: "CoreLane.exe.config"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\CoreLane"; Filename: "{app}\CoreLane.exe"; WorkingDir: "{app}"; Check: CanCreateStartMenuShortcut
Name: "{userdesktop}\CoreLane"; Filename: "{app}\CoreLane.exe"; WorkingDir: "{app}"; Tasks: desktopicon; Check: CanCreateDesktopShortcut

[Run]
Filename: "{app}\CoreLane.exe"; Description: "{cm:LaunchProgram,CoreLane}"; Flags: nowait postinstall skipifsilent unchecked

[Code]
function CanCreateStartMenuShortcut: Boolean;
begin
  Result := not WizardNoIcons and not FileExists(ExpandConstant('{userprograms}\CoreLane.lnk'));
end;

function CanCreateDesktopShortcut: Boolean;
begin
  Result := not WizardNoIcons and not FileExists(ExpandConstant('{userdesktop}\CoreLane.lnk'));
end;
