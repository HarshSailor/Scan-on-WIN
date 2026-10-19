#ifndef PublishDir
  #define PublishDir "..\artifacts\portable"
#endif
[Setup]
AppId={{BA92B96D-8DF1-4842-86C8-04D495F2B5BF}
AppName=Scan on WIN
AppVersion=1.0.1
AppPublisher=Scan on WIN contributors
DefaultDirName={localappdata}\Programs\ScanOnWIN
DefaultGroupName=Scan on WIN
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=ScanOnWIN_Setup
SetupIconFile=..\assets\ScanOnWIN.ico
UninstallDisplayIcon={app}\ScanOnWIN.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
LicenseFile=..\LICENSE
InfoAfterFile=..\docs\INSTALL-NOTES.txt
[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Ярлыки:"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\Scan on WIN"; Filename: "{app}\ScanOnWIN.exe"
Name: "{autodesktop}\Scan on WIN"; Filename: "{app}\ScanOnWIN.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\ScanOnWIN.exe"; Description: "Запустить Scan on WIN"; Flags: nowait postinstall skipifsilent
