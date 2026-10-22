#define MyAppName "ESTATÍSTICAS 15ª BPM"
#define MyAppVersion "1.1.7"
#define MyAppExeName "Estatisticas15BPM.exe"

[Setup]
AppId={{1D7435EE-0B3D-4AAE-9AFE-82D5DC493CFE}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\Estatisticas15BPM
DefaultGroupName={#MyAppName}
OutputDir=..\outputs
OutputBaseFilename=Instalador_Estatisticas15BPM_1.1.7
Compression=lzma2
SolidCompression=yes
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayName={#MyAppName}

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "..\Estatisticas15BPM\Assets\tco-icon-v2.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Dirs]
Name: "C:\Estatisticas15BPM\Dados"; Permissions: users-modify
Name: "C:\Estatisticas15BPM\Backup"; Permissions: users-modify

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\tco-icon-v2.ico"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\Assets\tco-icon-v2.ico"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Abrir {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;

