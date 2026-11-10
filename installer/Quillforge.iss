; Quillforge installer (Inno Setup 6). Built by build.ps1, which passes /DAppVersion and /DPublishDir.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish"
#endif

[Setup]
AppId={{8C1E2B57-4F0A-4D3B-9E62-5A1B0F0E2C71}
AppName=Quillforge
AppVersion={#AppVersion}
AppVerName=Quillforge {#AppVersion}
AppPublisher=Gabriel Castiglia
AppCopyright=© 2026 Gabriel Castiglia
DefaultDirName={localappdata}\Programs\Quillforge
DefaultGroupName=Quillforge
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=Quillforge-Setup-{#AppVersion}
SetupIconFile=..\src\Quillforge\Quillforge.ico
UninstallDisplayIcon={app}\Quillforge.exe
UninstallDisplayName=Quillforge
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Quillforge
VersionInfoDescription=Quillforge Setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AssocTask=Open .md files with Quillforge
es.AssocTask=Abrir los archivos .md con Quillforge
en.AssocGroup=File types:
es.AssocGroup=Tipos de archivo:
en.Manual=Quillforge user manual
es.Manual=Manual de usuario de Quillforge
en.MdDoc=Markdown document
es.MdDoc=Documento Markdown

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "{cm:AssocTask}"; GroupDescription: "{cm:AssocGroup}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Quillforge"; Filename: "{app}\Quillforge.exe"
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Quillforge_Manual_EN.pdf"; Languages: en
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Quillforge_Manual_ES.pdf"; Languages: es
Name: "{autodesktop}\Quillforge"; Filename: "{app}\Quillforge.exe"; Tasks: desktopicon

[Registry]
; ProgID
Root: HKA; Subkey: "Software\Classes\Quillforge.md"; ValueType: string; ValueData: "{cm:MdDoc}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Quillforge.md\DefaultIcon"; ValueType: string; ValueData: "{app}\Quillforge.exe,0"
Root: HKA; Subkey: "Software\Classes\Quillforge.md\shell\open\command"; ValueType: string; ValueData: """{app}\Quillforge.exe"" ""%1"""
; "Open with" list (always)
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Quillforge.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Quillforge.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Quillforge.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Quillforge.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Quillforge.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Quillforge.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
; Default handler (optional task)
Root: HKA; Subkey: "Software\Classes\.md"; ValueType: string; ValueData: "Quillforge.md"; Tasks: assoc; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueData: "Quillforge.md"; Tasks: assoc; Flags: uninsdeletevalue

[Run]
Filename: "{app}\Quillforge.exe"; Description: "{cm:LaunchProgram,Quillforge}"; Flags: nowait postinstall skipifsilent
