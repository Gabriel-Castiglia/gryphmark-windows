; Gryphmark installer (Inno Setup 6). Built by build.ps1, which passes /DAppVersion and /DPublishDir.
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish"
#endif

[Setup]
LicenseFile=..\LICENSE
AppId={{09A30F30-8B60-48A4-9A9A-1170F1578921}
AppName=Gryphmark
AppVersion={#AppVersion}
AppVerName=Gryphmark {#AppVersion}
AppPublisher=Gabriel Castiglia
AppCopyright=© 2026 Gabriel Castiglia
DefaultDirName={localappdata}\Programs\Gryphmark
DefaultGroupName=Gryphmark
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=Gryphmark-Setup-{#AppVersion}
SetupIconFile=..\src\Gryphmark\Gryphmark.ico
UninstallDisplayIcon={app}\Gryphmark.ico
UninstallDisplayName=Gryphmark
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Gryphmark
VersionInfoDescription=Gryphmark Setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AssocTask=Open .md files with Gryphmark
es.AssocTask=Abrir los archivos .md con Gryphmark
en.AssocGroup=File types:
es.AssocGroup=Tipos de archivo:
en.Manual=Gryphmark user manual
es.Manual=Manual de usuario de Gryphmark
en.MdDoc=Markdown document
en.FolderTask=Add "Open with Gryphmark" to folders in Explorer
es.FolderTask=Agregar «Abrir con Gryphmark» a las carpetas en el Explorador
en.FolderVerb=Open with Gryphmark
es.FolderVerb=Abrir con Gryphmark
en.ExplorerGroup=Explorer:
es.ExplorerGroup=Explorador:
es.MdDoc=Documento Markdown

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "assoc"; Description: "{cm:AssocTask}"; GroupDescription: "{cm:AssocGroup}"
Name: "foldermenu"; Description: "{cm:FolderTask}"; GroupDescription: "{cm:ExplorerGroup}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
; Sample document (Help → Sample document). Hidden, so it is not deleted by accident.
Source: "..\samples\sample_*.md"; DestDir: "{app}\samples"; Attribs: hidden; Flags: ignoreversion

[UninstallDelete]
; icon files written by View -> Theme
Type: filesandordirs; Name: "{app}\theme"

[Dirs]
Name: "{app}\samples"; Attribs: hidden

[Icons]
Name: "{autoprograms}\Gryphmark"; Filename: "{app}\Gryphmark.exe"; IconFilename: "{app}\Gryphmark.ico"
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Gryphmark_Manual_EN.pdf"; Languages: en
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Gryphmark_Manual_ES.pdf"; Languages: es
Name: "{autodesktop}\Gryphmark"; Filename: "{app}\Gryphmark.exe"; IconFilename: "{app}\Gryphmark.ico"; Tasks: desktopicon

[Registry]
; ProgID
Root: HKA; Subkey: "Software\Classes\Gryphmark.md"; ValueType: string; ValueData: "{cm:MdDoc}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Gryphmark.md\DefaultIcon"; ValueType: string; ValueData: "{app}\Gryphmark.ico"
Root: HKA; Subkey: "Software\Classes\Gryphmark.md\shell\open\command"; ValueType: string; ValueData: """{app}\Gryphmark.exe"" ""%1"""
; "Open with" list (always)
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Gryphmark.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Gryphmark.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Gryphmark.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Gryphmark.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Gryphmark.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Gryphmark.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
; Default handler (optional task)
Root: HKA; Subkey: "Software\Classes\.md"; ValueType: string; ValueData: "Gryphmark.md"; Tasks: assoc; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueData: "Gryphmark.md"; Tasks: assoc; Flags: uninsdeletevalue

; Folder context menu (optional task): opens the folder in the Files panel
Root: HKA; Subkey: "Software\Classes\Directory\shell\Gryphmark"; ValueType: string; ValueData: "{cm:FolderVerb}"; Tasks: foldermenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\Gryphmark"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Gryphmark.ico"; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Gryphmark\command"; ValueType: string; ValueData: """{app}\Gryphmark.exe"" ""%1"""; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Gryphmark"; ValueType: string; ValueData: "{cm:FolderVerb}"; Tasks: foldermenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Gryphmark"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Gryphmark.ico"; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Gryphmark\command"; ValueType: string; ValueData: """{app}\Gryphmark.exe"" ""%V"""; Tasks: foldermenu

[Run]
Filename: "{app}\Gryphmark.exe"; Description: "{cm:LaunchProgram,Gryphmark}"; Flags: nowait postinstall skipifsilent

[Code]
// Gryphmark was called Quillforge up to 1.1.1 (different AppId). Remove that install silently
// before copying the new files; its settings are copied over by the app on first start.
const
  OldKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E2B57-4F0A-4D3B-9E62-5A1B0F0E2C71}_is1';

function OldUninstaller(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, OldKey, 'UninstallString', S) or
     RegQueryStringValue(HKLM, OldKey, 'UninstallString', S) then
    Result := RemoveQuotes(S);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  U: String;
  Rc: Integer;
begin
  if CurStep = ssInstall then
  begin
    U := OldUninstaller();
    if (U <> '') and FileExists(U) then
      Exec(U, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Rc);
  end;
end;
