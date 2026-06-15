; -----------------------------------------------------------------------------
;  LecturIA - Inno Setup 6 installer script.
;
;  Produces a per-user installer that does not require administrator rights.
;  The application is copied to %LOCALAPPDATA%\Programs\LecturIA, with
;  shortcuts in the Start menu and (optionally) on the desktop.
;
;  Prerequisite: dotnet publish must have produced
;  publish\win-x64\LecturIA.exe before running this script.
;
;  Build with:
;      "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\LecturIA.iss
; -----------------------------------------------------------------------------

#ifndef AppVersion
    #define AppVersion "0.0.0"
#endif
#define AppName        "LecturIA"
#define AppPublisher   "Facultad de Ciencias Físicas y Matemáticas, Universidad de Chile"
#define AppExeName     "LecturIA.exe"
#define AppId          "{{3EF6DAD8-BF62-4D2A-8E70-9333B7DF14E7}"
#define SourceRoot     "..\publish\win-x64"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
OutputDir=..\dist
OutputBaseFilename=lecturia-recorder-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\LecturIA.App\Assets\app.ico
LicenseFile=..\LICENSE
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoProductName={#AppName}
CloseApplications=force
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; \
    GroupDescription: "Iconos adicionales:"; Flags: checkedonce

[Files]
Source: "{#SourceRoot}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
    Comment: "Grabación de lectura para evaluación de dominio lector"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; \
    Comment: "Grabación de lectura para evaluación de dominio lector"; \
    Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Iniciar {#AppName}"; \
    Flags: nowait postinstall skipifsilent
