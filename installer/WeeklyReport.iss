#define AppName "WeeklyReport"
#define AppVersion "0.1.0"
#define AppExeName "WeeklyReport.App.exe"
#define PublishDir "..\artifacts\publish\win-x64"
#define AppIcon "..\src\WeeklyReport.App\Assets\app.ico"

[Setup]
AppId={{81A541C0-A7C1-48CC-B681-8099EBE8C411}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=feainioh
AppPublisherURL=https://github.com/feainioh/WeeklyReport
DefaultDirName={localappdata}\Programs\WeeklyReport
DefaultGroupName=WeeklyReport
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
AppMutex=Local\WeeklyReport.Desktop
SetupIconFile={#AppIcon}
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\artifacts\installer
OutputBaseFilename=WeeklyReport-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WeeklyReport"; Filename: "{app}\{#AppExeName}"
Name: "{autoprograms}\Uninstall WeeklyReport"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch WeeklyReport"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  // [新增-卸载旧版] 本应用的卸载注册键（与 AppId 对应的 _is1 后缀）
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{81A541C0-A7C1-48CC-B681-8099EBE8C411}_is1';

// [新增-卸载旧版] 安装前优先卸载旧版本：
// 1) 结束正在运行的应用（单实例互斥量会阻止静默卸载覆盖）；
// 2) 静默执行旧版卸载器并等待应用目录清空（卸载器会自复制到临时目录，Exec 提前返回，
//    因此轮询等待目标 exe 消失，最多 20 秒）。
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  UninstallCommand: String;
  WaitIndex: Integer;
  AppExePath: String;
begin
  Result := '';
  NeedsRestart := False;
  AppExePath := ExpandConstant('{app}\{#AppExeName}');

  Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM {#AppExeName} >nul 2>&1',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  if RegQueryStringValue(HKCU, UninstallKey, 'UninstallString', UninstallCommand) then
  begin
    Exec(RemoveQuotes(UninstallCommand), '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    for WaitIndex := 1 to 40 do
    begin
      if not FileExists(AppExePath) then
        Break;
      Sleep(500);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
  ExistingCommand: String;
  ExpectedCommand: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    if FileExists(ExpandConstant('{app}\{#AppExeName}')) then
    begin
      if not Exec(ExpandConstant('{app}\{#AppExeName}'), '--unregister-notifications --quiet',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      begin
        Log('Could not start notification cleanup. Windows notification registration may remain.');
        SuppressibleMsgBox('Could not start notification cleanup. Windows notification registration may remain.',
          mbError, MB_OK, IDOK);
      end
      else if ResultCode <> 0 then
      begin
        Log('Notification cleanup failed. Windows notification registration may remain.');
        SuppressibleMsgBox('Notification cleanup failed. Windows notification registration may remain.',
          mbError, MB_OK, IDOK);
      end;
    end;

    ExpectedCommand := '"' + ExpandConstant('{app}\{#AppExeName}') + '"';
    if RegQueryStringValue(HKCU, RunKey, 'WeeklyReport', ExistingCommand) and
      (CompareText(ExistingCommand, ExpectedCommand) = 0) then
    begin
      if not RegDeleteValue(HKCU, RunKey, 'WeeklyReport') then
      begin
        Log('Could not remove the WeeklyReport autostart entry.');
        SuppressibleMsgBox('Could not remove the WeeklyReport autostart entry.', mbError, MB_OK, IDOK);
      end;
    end;
  end;
end;
