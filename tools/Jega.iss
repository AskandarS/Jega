; Jega's installer (Inno Setup 6). Built by tools\Pack.ps1, which passes the version and the staged bundle:
;
;   ISCC.exe /DVersion=0.9.0-beta.1 /DNumVersion=0.9.0.0 /DStage=<folder holding Jega.bundle> /O<output folder> Jega.iss
;
; Per user, no administrator: the bundle goes into %APPDATA%\Autodesk\ApplicationPlugins\Jega.bundle, where Revit 2025
; and 2026 find it themselves (the same place -p:DeployToRevit=true puts it). A new version installs over the old one;
; Contents\ is emptied first, so nothing stale is loaded beside the new DLLs. Uninstall removes the bundle and leaves
; %APPDATA%\Jega (the user's key, the log, the cross family) alone, as the README says. Revit must be closed: it holds
; Jega.dll open, so Setup asks to close it rather than closing it itself (unsaved models).

#ifndef Version
  #error Pass /DVersion=<Jega's version, e.g. 0.9.0-beta.1> (tools\Pack.ps1 does)
#endif
#ifndef NumVersion
  #error Pass /DNumVersion=<four numbers, e.g. 0.9.0.0>
#endif
#ifndef Stage
  #error Pass /DStage=<folder holding the staged Jega.bundle>
#endif

[Setup]
AppId={{422BB908-9740-45D1-BCA3-1625201275F1}
AppName=Jega
AppVersion={#Version}
AppVerName=Jega {#Version}
AppPublisher=Shamer Askandar
AppCopyright=Copyright (C) 2026 Shamer Askandar
VersionInfoVersion={#NumVersion}
VersionInfoDescription=Jega: from a Danish address to the site plan and terrain in Revit
DefaultDirName={userappdata}\Autodesk\ApplicationPlugins\Jega.bundle
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
OutputBaseFilename=Jega-{#Version}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=Jega {#Version} (Revit)
CloseApplications=no
RestartApplications=no

[Languages]
Name: "da"; MessagesFile: "compiler:Languages\Danish.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
da.CloseRevit=Revit er åben. Luk Revit, og tryk så på Forsøg igen.
en.CloseRevit=Revit is open. Close Revit, then press Retry.
da.KeyStays=Din Datafordeler-nøgle bliver i %APPDATA%\Jega, så en ny installation ikke spørger om den igen.
en.KeyStays=Your Datafordeler key stays in %APPDATA%\Jega, so a reinstall doesn't ask for it again.

[InstallDelete]
; Everything in Contents\ goes before the new build is copied: DLLs renamed while Revit held them, files of an older
; layout. The bundle's own files are all written again below.
Type: filesandordirs; Name: "{app}\Contents"
; Jega as an earlier deploy put it into the add-in folder, or Revit would load it twice.
Type: files; Name: "{userappdata}\Autodesk\Revit\Addins\2025\Jega.addin"
Type: filesandordirs; Name: "{userappdata}\Autodesk\Revit\Addins\2025\Jega"

[Files]
Source: "{#Stage}\Jega.bundle\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
function RevitRunning(): Boolean;
var
  Locator, Service, Processes: Variant;
begin
  Result := False;
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT Name FROM Win32_Process WHERE Name = ''Revit.exe''');
    Result := Processes.Count > 0;
  except
    { WMI not answering: don't block the install over it; the file copy fails loudly if Revit holds the DLL. }
    Result := False;
  end;
end;

{ Asks until Revit is closed, or gives up when the user cancels. }
function RevitClosedOrCancelled(): Boolean;
begin
  Result := True;
  while RevitRunning() do
  begin
    if MsgBox(CustomMessage('CloseRevit'), mbError, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := RevitClosedOrCancelled();
end;

function InitializeUninstall(): Boolean;
begin
  Result := RevitClosedOrCancelled();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    MsgBox(CustomMessage('KeyStays'), mbInformation, MB_OK);
end;
