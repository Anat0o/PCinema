#define MyAppName "PCinema"
#define MyAppVersion "1.0.0"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
[Setup]
AppId={{8B5DF747-9B35-4E21-87A9-244F2C774E0C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\PCinema
DefaultGroupName=PCinema
PrivilegesRequired=lowest
MinVersion=10.0.22000
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts
OutputBaseFilename=PCinema-Setup-1.0.0
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\PCinema\Assets\pcinema.ico
UninstallDisplayIcon={app}\PCinema.exe
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\PCinema"; Filename: "{app}\PCinema.exe"
Name: "{autodesktop}\PCinema"; Filename: "{app}\PCinema.exe"; Tasks: desktopicon
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; Flags: unchecked
[Run]
Filename: "{app}\PCinema.exe"; Description: "Запустить PCinema"; Flags: nowait postinstall skipifsilent

