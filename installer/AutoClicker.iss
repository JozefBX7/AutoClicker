#ifndef MyAppVersion
  #define MyAppVersion GetFileVersion("..\artifacts\installer\AutoClicker.exe")
#endif

[Setup]
AppId={{A4D1A5BA-24E0-4E0D-AD8F-0AD7AA7871B8}
AppName=AutoClicker
AppVersion={#MyAppVersion}
AppPublisher=JBX7
AppPublisherURL=https://github.com/JozefBX7/AutoClicker
AppSupportURL=https://github.com/JozefBX7/AutoClicker/issues
DefaultDirName={autopf}\AutoClicker
DefaultGroupName=AutoClicker
DisableProgramGroupPage=yes
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=AutoClicker-Setup-x64
SetupIconFile=..\Assets\AutoClickerIcon.ico
UninstallDisplayIcon={app}\AutoClicker.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern dynamic
WizardSizePercent=100,100
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany=JBX7
VersionInfoDescription=AutoClicker Setup
VersionInfoProductName=AutoClicker

; To sign both setup and uninstaller, pass /DSignTool="signtool" to ISCC.
#ifdef SignTool
SignTool={#SignTool}
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\installer\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\AutoClicker"; Filename: "{app}\AutoClicker.exe"
Name: "{autodesktop}\AutoClicker"; Filename: "{app}\AutoClicker.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AutoClicker.exe"; Description: "Launch AutoClicker"; Flags: nowait postinstall skipifsilent
