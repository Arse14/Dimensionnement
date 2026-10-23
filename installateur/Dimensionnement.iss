; Installateur Windows de Dimensionnement électrique (Inno Setup 6).
; Compilé par la CI : iscc /DVersion=1.0.0 /DSource=publication installateur\Dimensionnement.iss

#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef Source
  #define Source "..\publication"
#endif

[Setup]
AppId={{A71CF98D-4184-440F-89BD-90200A9DD3B5}
AppName=Dimensionnement électrique
AppVersion={#Version}
AppVerName=Dimensionnement électrique {#Version}
AppPublisher=ITP INTERPIPE
DefaultDirName={autopf}\ITP INTERPIPE\Dimensionnement
DefaultGroupName=ITP INTERPIPE
DisableProgramGroupPage=yes
OutputBaseFilename=Dimensionnement-v{#Version}-installation
SetupIconFile=..\src\Dimensionnement.App\Ressources\icone.ico
UninstallDisplayIcon={app}\Dimensionnement.App.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Installation pour tous les utilisateurs, ou pour soi seul sans droits administrateur.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "bureau"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Raccourcis :"
Name: "association"; Description: "Ouvrir les projets .dimelec avec Dimensionnement électrique"; GroupDescription: "Fichiers :"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Dimensionnement électrique"; Filename: "{app}\Dimensionnement.App.exe"
Name: "{autodesktop}\Dimensionnement électrique"; Filename: "{app}\Dimensionnement.App.exe"; Tasks: bureau

[Registry]
Root: HKA; Subkey: "Software\Classes\.dimelec"; ValueType: string; ValueData: "Dimensionnement.Projet"; Flags: uninsdeletevalue; Tasks: association
Root: HKA; Subkey: "Software\Classes\Dimensionnement.Projet"; ValueType: string; ValueData: "Projet de dimensionnement électrique"; Flags: uninsdeletekey; Tasks: association
Root: HKA; Subkey: "Software\Classes\Dimensionnement.Projet\DefaultIcon"; ValueType: string; ValueData: "{app}\Dimensionnement.App.exe,0"; Tasks: association
Root: HKA; Subkey: "Software\Classes\Dimensionnement.Projet\shell\open\command"; ValueType: string; ValueData: """{app}\Dimensionnement.App.exe"" ""%1"""; Tasks: association

[Run]
Filename: "{app}\Dimensionnement.App.exe"; Description: "Lancer Dimensionnement électrique"; Flags: nowait postinstall skipifsilent
; Mise à jour lancée depuis l'application (/RELANCER=1) : l'application redémarre une fois installée, sous l'utilisateur.
Filename: "{app}\Dimensionnement.App.exe"; Flags: nowait runasoriginaluser; Check: Relancer

[Code]
function Relancer: Boolean;
begin
  Result := ExpandConstant('{param:RELANCER|0}') = '1';
end;
