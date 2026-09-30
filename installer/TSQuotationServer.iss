; TS Quotation Server — installs the central Quotation Server as a Windows Service.
; Build: ISCC.exe /DAppVersion=0.9.0 TSQuotationServer.iss   (after installer\publish.ps1)
#ifndef AppVersion
  #define AppVersion "0.9.0"
#endif
#define ServiceName "TSQuotationServer"

[Setup]
AppId={{7C1B1E7A-5D41-4B2E-9C11-2D5A1F0E0A01}
AppName=TS Quotation Server
AppVersion={#AppVersion}
AppPublisher=T.SAIFUDDIN & CO.
DefaultDirName={autopf}\TSQuotation\Server
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\publish\installers
OutputBaseFilename=TSQuotationServer-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=TS Quotation Server

[Files]
Source: "..\publish\server\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "appsettings.json"
; Keep an existing configuration on upgrade.
Source: "..\publish\server\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "..\publish\simulator\TallySimulator.exe"; DestDir: "{app}\tools"; Flags: ignoreversion
Source: "..\docs\SETUP.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Dirs]
; Database, PDFs, logs and backups. Never removed on uninstall.
Name: "{commonappdata}\TSQuotation"; Permissions: system-full admins-full; Flags: uninsneveruninstall

[Run]
Filename: "{sys}\sc.exe"; Parameters: "create {#ServiceName} binPath= ""{app}\TSQuotation.Server.exe"" start= delayed-auto DisplayName= ""TS Quotation Server"""; Flags: runhidden waituntilterminated; Check: not ServiceExists
Filename: "{sys}\sc.exe"; Parameters: "description {#ServiceName} ""Central quotation server (TallyPrime integration, numbering, PDF, AI, Gmail)"""; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "failure {#ServiceName} reset= 86400 actions= restart/60000/restart/60000/restart/60000"; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""TS Quotation Server"""; Flags: runhidden waituntilterminated
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""TS Quotation Server"" dir=in action=allow protocol=TCP localport=5080 profile=domain,private"; Flags: runhidden waituntilterminated
Filename: "{sys}\sc.exe"; Parameters: "start {#ServiceName}"; Flags: runhidden waituntilterminated; StatusMsg: "Starting the Quotation Server…"

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete {#ServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""TS Quotation Server"""; Flags: runhidden waituntilterminated; RunOnceId: "DeleteFirewall"

[Code]
function ServiceExists: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Stop the running service so its files can be replaced during an upgrade.
  if ServiceExists then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#ServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(4000);
  end;
  Result := '';
end;
