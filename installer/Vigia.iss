; Vigía installer (Inno Setup 6). Built by build.ps1, which passes /DAppVersion and /DPublishDir.
; Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
; Date: November 10, 2026
; SPDX-License-Identifier: GPL-2.0-only
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\out\publish"
#endif

[Setup]
LicenseFile=..\LICENSE
AppId={{5894F16C-0351-48A8-B43C-F069EC7368FF}
AppName=Vigía
AppVersion={#AppVersion}
AppVerName=Vigía {#AppVersion}
AppPublisher=Gabriel Castiglia
AppCopyright=© 2026 Gabriel Castiglia
DefaultDirName={localappdata}\Programs\Vigia
DefaultGroupName=Vigía
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\out
OutputBaseFilename=Vigia-Setup-{#AppVersion}
SetupIconFile=..\src\Vigia\Vigia.ico
UninstallDisplayIcon={app}\Vigia.ico
UninstallDisplayName=Vigía
WizardStyle=modern
WizardImageFile=wizard.bmp
WizardSmallImageFile=wizard-small.bmp
Compression=lzma2/ultra64
SolidCompression=yes
ChangesAssociations=yes
CloseApplications=yes
VersionInfoVersion={#AppVersion}
VersionInfoProductName=Vigía
VersionInfoDescription=Vigía Setup

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[CustomMessages]
en.AssocTask=Open .md files with Vigía
es.AssocTask=Abrir los archivos .md con Vigía
en.AssocGroup=File types:
es.AssocGroup=Tipos de archivo:
en.Manual=Vigía user manual
es.Manual=Manual de usuario de Vigía
en.MdDoc=Markdown document
en.FolderTask=Add "Open with Vigía" to folders in Explorer
es.FolderTask=Agregar «Abrir con Vigía» a las carpetas en el Explorador
en.FolderVerb=Open with Vigía
es.FolderVerb=Abrir con Vigía
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
Name: "{autoprograms}\Vigía"; Filename: "{app}\Vigia.exe"; IconFilename: "{app}\Vigia.ico"
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Vigia_Manual_EN.pdf"; Languages: en
Name: "{autoprograms}\{cm:Manual}"; Filename: "{app}\docs\Vigia_Manual_ES.pdf"; Languages: es
Name: "{autodesktop}\Vigía"; Filename: "{app}\Vigia.exe"; IconFilename: "{app}\Vigia.ico"; Tasks: desktopicon

[Registry]
; ProgID
Root: HKA; Subkey: "Software\Classes\Vigia.md"; ValueType: string; ValueData: "{cm:MdDoc}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Vigia.md\DefaultIcon"; ValueType: string; ValueData: "{app}\Vigia.ico"
Root: HKA; Subkey: "Software\Classes\Vigia.md\shell\open\command"; ValueType: string; ValueData: """{app}\Vigia.exe"" ""%1"""
; "Open with" list (always)
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "Vigia.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "Vigia.md"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\Vigia.exe\shell\open\command"; ValueType: string; ValueData: """{app}\Vigia.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\Vigia.exe\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\Vigia.exe\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""
; Default handler (optional task)
Root: HKA; Subkey: "Software\Classes\.md"; ValueType: string; ValueData: "Vigia.md"; Tasks: assoc; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown"; ValueType: string; ValueData: "Vigia.md"; Tasks: assoc; Flags: uninsdeletevalue

; Folder context menu (optional task): opens the folder in the Files panel
Root: HKA; Subkey: "Software\Classes\Directory\shell\Vigia"; ValueType: string; ValueData: "{cm:FolderVerb}"; Tasks: foldermenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\shell\Vigia"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Vigia.ico"; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\shell\Vigia\command"; ValueType: string; ValueData: """{app}\Vigia.exe"" ""%1"""; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Vigia"; ValueType: string; ValueData: "{cm:FolderVerb}"; Tasks: foldermenu; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Vigia"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\Vigia.ico"; Tasks: foldermenu
Root: HKA; Subkey: "Software\Classes\Directory\Background\shell\Vigia\command"; ValueType: string; ValueData: """{app}\Vigia.exe"" ""%V"""; Tasks: foldermenu

[Run]
Filename: "{app}\Vigia.exe"; Description: "{cm:LaunchProgram,Vigía}"; Flags: nowait postinstall skipifsilent
; Help -> Check for updates installs in silence and asks for the windows that were open to come back
Filename: "{app}\Vigia.exe"; Parameters: "--after-update"; Flags: nowait; Check: GmRelaunch

[Code]
// Started by Vigía itself (MainWindow.Update.cs): /GMWAITPID=<pid> waits for that window to finish closing
// before any file is replaced; /GMRELAUNCH=1 reopens the windows that were open (Vigía --after-update).
function OpenProcess(Access: Cardinal; Inherit: Boolean; Pid: Cardinal): Cardinal;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(H: Cardinal; Ms: Cardinal): Cardinal;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(H: Cardinal): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function InitializeSetup(): Boolean;
var
  Pid: Integer;
  H: Cardinal;
begin
  Pid := StrToIntDef(ExpandConstant('{param:GMWAITPID|0}'), 0);
  if Pid > 0 then
  begin
    H := OpenProcess($00100000, False, Pid);   // SYNCHRONIZE
    if H <> 0 then
    begin
      WaitForSingleObject(H, 60000);
      CloseHandle(H);
    end;
  end;
  Result := True;
end;

function GmRelaunch(): Boolean;
begin
  Result := ExpandConstant('{param:GMRELAUNCH|0}') = '1';
end;

// Vigía was called Gryphmark up to 2.2.1 and Quillforge up to 1.1.1, each with an AppId of its own. Those installs
// are removed silently before the new files are copied; their settings are copied over by the app on first start.
const
  QuillforgeKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8C1E2B57-4F0A-4D3B-9E62-5A1B0F0E2C71}_is1';
  GryphmarkKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{09A30F30-8B60-48A4-9A9A-1170F1578921}_is1';

function OldUninstaller(Key: String): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, Key, 'UninstallString', S) or
     RegQueryStringValue(HKLM, Key, 'UninstallString', S) then
    Result := RemoveQuotes(S);
end;

procedure RemoveOld(Key: String);
var
  U: String;
  Rc: Integer;
begin
  U := OldUninstaller(Key);
  if (U <> '') and FileExists(U) then
    Exec(U, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Rc);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
    RemoveOld(QuillforgeKey);
    RemoveOld(GryphmarkKey);
  end;
end;
