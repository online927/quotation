; TS Quotation — desktop application for client PCs.
; Build: ISCC.exe /DAppVersion=0.9.0 TSQuotationClient.iss   (after installer\publish.ps1)
; Silent install with server address:  TSQuotation-Setup.exe /VERYSILENT /SERVER=http://server-pc:5080
#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif

[Setup]
AppId={{7C1B1E7A-5D41-4B2E-9C11-2D5A1F0E0A02}
AppName=TS Quotation
AppVersion={#AppVersion}
AppPublisher=T.SAIFUDDIN & CO.
DefaultDirName={autopf}\TSQuotation\Client
DefaultGroupName=TS Quotation
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installers
OutputBaseFilename=TSQuotation-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"

[Files]
Source: "..\publish\desktop\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\TS Quotation"; Filename: "{app}\TSQuotation.exe"
Name: "{autodesktop}\TS Quotation"; Filename: "{app}\TSQuotation.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TSQuotation.exe"; Description: "Start TS Quotation"; Flags: nowait postinstall skipifsilent

[Code]
var
  ServerPage: TInputQueryWizardPage;

procedure InitializeWizard;
begin
  ServerPage := CreateInputQueryPage(wpSelectDir, 'Quotation Server', 'Where is the Quotation Server installed?',
    'Enter the address of the PC running the TS Quotation Server (for example http://SERVER-PC:5080). ' +
    'Each user can change it later on the login screen.');
  ServerPage.Add('Server address:', False);
  ServerPage.Values[0] := ExpandConstant('{param:SERVER|http://localhost:5080}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Json: String;
begin
  if CurStep = ssPostInstall then
  begin
    // Default server address for users who have not configured one yet.
    Json := '{' + #13#10 + '  "ServerUrl": "' + ServerPage.Values[0] + '"' + #13#10 + '}';
    SaveStringToFile(ExpandConstant('{app}\client.defaults.json'), Json, False);
  end;
end;
