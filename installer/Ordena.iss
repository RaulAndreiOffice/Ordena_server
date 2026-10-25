; Ordena's installer (Inno Setup 6). installer\build.ps1 compiles it after publishing, passing the version
; and the folders; run that script rather than this file by hand:
;   iscc /DAppVersion=1.0.0 /DPublishDir=..\artifacts\publish /DOutputDir=..\artifacts Ordena.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
; Never change AppId: a later setup with the same one upgrades this installation in place.
AppId={{AF88A4F7-2826-42F3-A19B-4D8A36220190}
AppName=Ordena
AppVersion={#AppVersion}
AppVerName=Ordena {#AppVersion}
AppPublisher=mind2code.dev
AppPublisherURL=https://mind2code.dev
VersionInfoVersion={#AppVersion}
; For the current user only, so no administrator is needed: %LocalAppData%\Programs\Ordena.
PrivilegesRequired=lowest
DefaultDirName={autopf}\Ordena
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\src\Ordena.Frontend\Resources\ordena.ico
UninstallDisplayIcon={app}\Ordena.exe
UninstallDisplayName=Ordena
OutputDir={#OutputDir}
OutputBaseFilename=OrdenaSetup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; Spanish on a Spanish Windows, English otherwise; the language list shows only when neither matches.
ShowLanguageDialog=auto
; An update asks to close a running Ordena first, so its files can be replaced.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Ordena"; Filename: "{app}\Ordena.exe"
Name: "{autodesktop}\Ordena"; Filename: "{app}\Ordena.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Ordena.exe"; Description: "{cm:LaunchProgram,Ordena}"; Flags: nowait postinstall skipifsilent

; Uninstalling removes only what this setup installed. The database and settings (%LocalAppData%\Ordena)
; and the invoices folder belong to the customer and stay where they are: no [UninstallDelete] on purpose.
