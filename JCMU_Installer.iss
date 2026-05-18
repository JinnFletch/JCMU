#define AppName "Jinn Context Menu Utility (JCMU)"
#define AppVersion GetVersionNumbersString("JCMU.ConsoleBed\bin\Release\net8.0\win-x64\publish\jcmu.exe")

[Setup]
AppId={{YOUR-GUID-HERE}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
DefaultDirName={commonpf}\JCMU
VersionInfoVersion={#AppVersion}
AppPublisher=Jinn Studios
DisableProgramGroupPage=yes
OutputBaseFilename=JCMU_Installer
Compression=lzma
SolidCompression=yes
ChangesEnvironment=yes
; Changed to admin because we are writing to {commonpf}
PrivilegesRequired=admin
OutputDir=Output

[Files]
; This grabs the jcmu.exe AND the Icons folder automatically
Source: "JCMU.ConsoleBed\bin\Release\net8.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Registry]
; Adds the installation folder to the User's PATH Environment Variable
Root: HKCU; Subkey: "Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Check: NeedsAddPath(ExpandConstant('{app}'))

[Code]
// A standard Pascal script to ensure we don't accidentally add the path twice if they run the installer twice
function NeedsAddPath(Param: string): boolean;
var
  OrigPath: string;
begin
  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath)
  then begin
    Result := True;
    exit;
  end;
  Result := Pos(';' + Param + ';', ';' + OrigPath + ';') = 0;
end;

[Run]
; Executes 'jcmu init' automatically at the end of the installation.
; Flags: runhidden ensures the user doesn't see a black console window flash.
Filename: "{app}\jcmu.exe"; Parameters: "init"; StatusMsg: "Initializing JCMU Core Registry Anchors..."; Flags: runhidden