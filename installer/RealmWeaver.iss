#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#define MyAppName "RealmWeaver"
#define MyAppPublisher "Akz0rt"
#define MyAppExeName "RealmWeaver.exe"
#define MyAppMutex "RealmWeaverSingleInstanceMutex"

[Setup]
AppId={{73312D00-F8DE-4552-9DB0-16AA78F9B7E1}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppMutex={#MyAppMutex}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=RealmWeaver-Setup-{#MyAppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=yes
DisableProgramGroupPage=yes

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Files]
Source: "..\build\StandaloneWindows64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Запустить {#MyAppName}"; Flags: nowait postinstall skipifsilent
