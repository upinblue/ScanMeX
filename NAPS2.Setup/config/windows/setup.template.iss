; !defs

#include "..\config\windows\setup.languages.iss"

#define AppShortName             "ScanMe"
#define AppLongName              "ScanMe"
#define AppCompany               "up in blue GmbH"
#define AppCopyrightStartYear    "2025"
#define AppCopyrightEndYear      GetDateTimeString('yyyy','','')
; The copyright holder of ScanMe is the company that publishes it, not a contributor pool. This read
; "ScanMe Contributors" -- a rebranding of upstream's "NAPS2 Contributors" -- which named a body that
; does not exist while dropping the attribution the GPL requires be kept. NAPS2's own copyright is
; retained where it belongs, in LICENSE and the per-project LICENSE files.
#define AppCopyrightCompany      "up in blue GmbH"
#define ExeName                  "ScanMe.exe"

[Setup]
; The AppId is what Inno matches against to detect an existing installation and upgrade it in place.
; Without it Inno falls back to AppName, so this is the value already used by installed versions --
; setting it explicitly keeps upgrades working even if the displayed name ever changes.
AppId=ScanMe
AppName={#AppLongName}
AppVersion={#AppVersion}
AppVerName={#AppShortName} {#AppVersionName}
AppPublisher={#AppCompany}
AppPublisherURL=https://www.upinblue.com
AppSupportURL=https://www.upinblue.com/contact
AppUpdatesURL=https://www.upinblue.com/download

VersionInfoDescription={#AppShortName} installer
VersionInfoVersion={#AppVersion}
VersionInfoProductName={#AppShortName}
VersionInfoProductVersion={#AppVersion}
VersionInfoCompany={#AppCompany}
VersionInfoCopyright=Copyright (c) {#AppCopyrightStartYear}-{#AppCopyrightEndYear} {#AppCopyrightCompany}

ShowLanguageDialog=yes
UsePreviousLanguage=no
LanguageDetectionMethod=uilanguage
WizardStyle=modern
; Require Windows 10 1607+
MinVersion=10.0.14393

DefaultDirName={commonpf}\{#AppShortName}
DefaultGroupName={#AppShortName}
LicenseFile=..\..\LICENSE

UninstallDisplayName={#AppShortName}
UninstallDisplayIcon={app}\{#ExeName}

OutputDir=../publish/{#AppVersionName}
OutputBaseFilename=ScanMe-{#AppVersionName}-{#AppPlatform}
Compression=lzma2/ultra64
LZMAUseSeparateProcess=yes
SolidCompression=yes
; !arch

ChangesAssociations=yes

[Run]
Filename: "{app}\{#ExeName}"; Flags: nowait postinstall

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]                              
; !files

; Delete files from old locations in case of upgrade
[InstallDelete]     
Type: files; Name: "{app}\*.exe"
Type: files; Name: "{app}\*.exe.config"
Type: files; Name: "{app}\*.dll"
Type: files; Name: "{app}\*.json"
Type: filesandordirs; Name: "{app}\lib"
; !clean32

[Icons]
; The shortcuts carry the app's own name -- they were still called NAPS2, so the EXE installer put a
; "NAPS2" entry in the Start menu group and on the desktop for a product called ScanMe.
Name: "{group}\{#AppShortName}"; Filename: "{app}\{#ExeName}"
Name: "{commondesktop}\{#AppShortName}"; Filename: "{app}\{#ExeName}"; Tasks: desktopicon

[Registry]
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Action"; ValueData: "Scan with ScanMe"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "CLSID"; ValueData: "WIACLSID"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "DefaultIcon"; ValueData: "sti.dll,0"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "InitCmdLine"; ValueData: "/WiaCmd;{app}\{#ExeName} /StiDevice:%1 /StiEvent:%2;"
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\AutoplayHandlers\Handlers\WIA_{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Provider"; ValueData: "ScanMe"

Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\StillImage\Registered Applications"; Flags:uninsdeletevalue; ValueType: string; ValueName: "ScanMe"; ValueData: "{app}\{#ExeName}"

Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\StillImage\Events\STIProxyEvent\{{1c3a7177-f3a7-439e-be47-e304a185f932}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\StillImage\Events\STIProxyEvent\{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Cmdline"; ValueData: "{app}\{#ExeName} /StiDevice:%1 /StiEvent:%2"
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\StillImage\Events\STIProxyEvent\{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Desc"; ValueData: "Scan with ScanMe"
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\StillImage\Events\STIProxyEvent\{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#ExeName},0"
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\StillImage\Events\STIProxyEvent\{{1c3a7177-f3a7-439e-be47-e304a185f932}"; ValueType: string; ValueName: "Name"; ValueData: "ScanMe"

Root: HKCR; Subkey: ".pdf\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".jpg\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".jpeg\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".png\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".tiff\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".tif\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: ".bmp\OpenWithProgids"; ValueType: string; ValueName: "{#AppShortName}"; ValueData: ""; Flags: uninsdeletevalue
Root: HKCR; Subkey: "{#AppShortName}"; ValueType: string; ValueName: ""; ValueData: "{#AppShortName}"; Flags: uninsdeletekey;
Root: HKCR; Subkey: "{#AppShortName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#ExeName},0"
Root: HKCR; Subkey: "{#AppShortName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#ExeName}"" ""%1"""