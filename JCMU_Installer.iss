[Setup]
AppName=Jinn Context Menu Utility (JCMU)
AppVersion=1.0.0
AppPublisher=Jinn Studios
; Installs to the User's local AppData folder so they don't need Admin rights to install it
DefaultDirName={localappdata}\Programs\JCMU
DisableProgramGroupPage=yes
OutputBaseFilename=JCMU_Installer
Compression=lzma
SolidCompression=yes
; Tells Windows Explorer to refresh the PATH immediately so no reboot is required
ChangesEnvironment=yes
PrivilegesRequired=lowest

[Files]
; The main executable
Source: "JCMU.ConsoleBed\bin\Release\net8.0\win-x64\publish\jcmu.exe"; DestDir: "{app}"; Flags: ignoreversion
; The icons folder
Source: "JCMU.ConsoleBed\bin\Release\net8.0\win-x64\publish\Icons\*"; DestDir: "{app}\Icons"; Flags: ignoreversion recursesubdirs 

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