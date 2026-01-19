; HyTaLauncher Installer Script for Inno Setup
; Compile with Inno Setup 6.x

#define MyAppName "HyTaLauncher"
#define MyAppVersion "1.0.7"
#define MyAppPublisher "HyTaLauncher"
#define MyAppURL "https://github.com/MerryJoyKey-Studio/HyTaLauncher"
#define MyAppExeName "HyTaLauncher.exe"

[Setup]
AppId={{8F4E9A2B-3C5D-4E6F-A1B2-C3D4E5F6A7B8}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={userappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=
OutputDir=installer
OutputBaseFilename=HyTaLauncher_Setup_{#MyAppVersion}
SetupIconFile=Resources\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
AlwaysRestart=no
CloseApplications=yes
RestartApplications=yes
CloseApplicationsFilter=*.exe

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Components]
Name: "main"; Description: "HyTaLauncher"; Types: full compact custom; Flags: fixed

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\HyTaLauncher.exe"; DestDir: "{app}"; Flags: replacesameversion restartreplace
Source: "Fonts\*"; DestDir: "{app}\Fonts"; Flags: replacesameversion recursesubdirs createallsubdirs 
Source: "Languages\*"; DestDir: "{app}\Languages"; Flags: replacesameversion recursesubdirs createallsubdirs
Source: "Addons\*"; DestDir: "{app}\Addons"; Flags: replacesameversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\HyTaLauncher"

[Code]
var
  DeleteGameCheckbox: TNewCheckBox;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Проверяем, запущен ли лаунчер
  if CheckForMutexes('HyTaLauncher') then
  begin
    if MsgBox('HyTaLauncher is currently running. Please close it before continuing installation.' + #13#10#13#10 + 
              'Do you want to close it now?', mbConfirmation, MB_YESNO) = IDYES then
    begin
      // Пытаемся закрыть процесс
      Exec('taskkill', '/F /IM HyTaLauncher.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      Sleep(1000); // Ждём завершения процесса
      Result := True;
    end
    else
    begin
      Result := False;
    end;
  end
  else
  begin
    Result := True;
  end;
end;

procedure InitializeUninstallProgressForm();
begin
  DeleteGameCheckbox := TNewCheckBox.Create(UninstallProgressForm);
  DeleteGameCheckbox.Parent := UninstallProgressForm;
  DeleteGameCheckbox.Caption := 'Also delete game files (Hytale)';
  DeleteGameCheckbox.Checked := False;
  DeleteGameCheckbox.Left := ScaleX(20);
  DeleteGameCheckbox.Top := UninstallProgressForm.StatusLabel.Top + UninstallProgressForm.StatusLabel.Height + ScaleY(20);
  DeleteGameCheckbox.Width := ScaleX(300);
  DeleteGameCheckbox.Height := ScaleY(20);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  GamePath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    if DeleteGameCheckbox.Checked then
    begin
      GamePath := ExpandConstant('{userappdata}\Hytale');
      if DirExists(GamePath) then
      begin
        if MsgBox('Are you sure you want to delete all game files?' + #13#10 + 
                  'Path: ' + GamePath + #13#10#13#10 +
                  'This action cannot be undone!', 
                  mbConfirmation, MB_YESNO) = IDYES then
        begin
          DelTree(GamePath, True, True, True);
        end;
      end;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
end;
