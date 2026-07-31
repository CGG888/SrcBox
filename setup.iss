; Script generated for Inno Setup
; SEE THE DOCUMENTATION FOR DETAILS ON CREATING INNO SETUP SCRIPT FILES!

#define MyAppName "SrcBox"
#define MyAppVersion "1.1.5"
#define MyAppPublisher "CGG888"
#define MyAppExeName "SrcBox.exe"

#define DefaultRepoURL "https://github.com/CGG888/SrcBox"
#define GitOriginURL GetEnv("GIT_ORIGIN_URL")
#define GitRepoURLEnv GetEnv("GIT_REPO_URL")
#define GitIssuesURLEnv GetEnv("GIT_ISSUES_URL")
#define GitReleasesURLEnv GetEnv("GIT_RELEASES_URL")
#define GitDescribe GetEnv("GIT_DESCRIBE")

#if GitRepoURLEnv == ""
  #define GitRepoURL DefaultRepoURL
#else
  #define GitRepoURL GitRepoURLEnv
#endif

#if GitIssuesURLEnv == ""
  #define GitIssuesURL GitRepoURL + "/issues"
#else
  #define GitIssuesURL GitIssuesURLEnv
#endif

#if GitReleasesURLEnv == ""
  #define GitReleasesURL GitRepoURL + "/releases"
#else
  #define GitReleasesURL GitReleasesURLEnv
#endif

#if GitDescribe != ""
  #undef MyAppVersion
  #define MyAppVersion GitDescribe
#endif

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{A1B2C3D4-E5F6-7890-1234-56789ABCDEF0}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#GitRepoURL}
AppSupportURL={#GitIssuesURL}
AppUpdatesURL={#GitReleasesURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
; Remove the following line to run in administrative install mode (install for all users.)
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=SrcBox_Setup_{#MyAppVersion}
SetupIconFile=srcbox.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
LicenseFile=LICENSE.txt
InfoBeforeFile=THIRD-PARTY-NOTICES.txt

[CustomMessages]
english.OpenGitHubRepo=Open GitHub repository after setup
chinesesimplified.OpenGitHubRepo=安装完成后打开 GitHub 仓库主页
english.GitHubHelp=For help or feedback, visit Issues: {#GitIssuesURL}
chinesesimplified.GitHubHelp=如需帮助或反馈，请访问 Issues：{#GitIssuesURL}

[Messages]
english.WelcomeLabel2=This wizard will install {#MyAppName} {#MyAppVersion}. Project: {#GitRepoURL}
chinesesimplified.WelcomeLabel2=该向导将安装 {#MyAppName} {#MyAppVersion}。项目主页：{#GitRepoURL}
english.FinishedLabel=Setup has finished installing {#MyAppName}. {cm:GitHubHelp}
chinesesimplified.FinishedLabel=安装完成 {#MyAppName}。{cm:GitHubHelp}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; IMPORTANT: Make sure you run 'dotnet publish' before compiling this script!
Source: "bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files
Source: "LICENSE.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{#GitRepoURL}"; Description: "{cm:OpenGitHubRepo}"; Flags: nowait postinstall skipifsilent shellexec unchecked
