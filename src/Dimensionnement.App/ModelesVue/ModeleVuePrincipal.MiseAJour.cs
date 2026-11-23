using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.App.Vues;
using Dimensionnement.Core.Persistance;

namespace Dimensionnement.App.ModelesVue;

/// <summary>
/// Mise à jour automatique : au démarrage, la dernière release GitHub est comparée à la version installée ; un bandeau
/// propose la mise à jour, téléchargée puis installée depuis l'application, qui redémarre sur la nouvelle version.
/// </summary>
public partial class ModeleVuePrincipal
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    VersionDisponible? _versionDisponible;
    bool _miseAJourMasquee, _telechargement;
    double _avancement;
    string _etatMiseAJour = "";

    public static Version VersionActuelle => MiseAJour.Normaliser(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));
    public string VersionTexte => $"v{VersionActuelle.ToString(VersionActuelle.Build > 0 ? 3 : 2)}";

    /// <summary>Installée par l'installateur (désinstallateur présent à côté de l'exécutable) ou version portable.</summary>
    static bool Installee => File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    public VersionDisponible? VersionDisponible => _versionDisponible;
    public bool MiseAJourVisible => _versionDisponible is not null && !_miseAJourMasquee;
    public string TexteMiseAJour => _versionDisponible is { } v
        ? $"Nouvelle version {v.Etiquette} disponible (tu as la {VersionTexte})." : "";
    public bool Telechargement { get => _telechargement; private set { _telechargement = value; Signaler(); } }
    public double AvancementMiseAJour { get => _avancement; private set { _avancement = value; Signaler(); } }
    public string EtatMiseAJour { get => _etatMiseAJour; private set { _etatMiseAJour = value; Signaler(); } }

    Commande? _installerMiseAJour, _masquerMiseAJour, _notesMiseAJour, _ouvrirNouveautes;
    public Commande OuvrirNouveautes => _ouvrirNouveautes ??= new Commande(() =>
        new FenetreNouveautes(VersionActuelle, _versionDisponible) { Owner = Application.Current.MainWindow }.Show());
    public Commande InstallerMiseAJour => _installerMiseAJour ??= new Commande(async () => await Installer(), () => !Telechargement);
    public Commande MasquerMiseAJour => _masquerMiseAJour ??= new Commande(() => { _miseAJourMasquee = true; Signaler(nameof(MiseAJourVisible)); });
    public Commande NotesMiseAJour => _notesMiseAJour ??= new Commande(() => OuvrirNouveautes.Execute(null));

    /// <summary>Dernière version dont la page « Nouveautés » a été montrée.</summary>
    static readonly string CheminVersionVue = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", "version-vue.txt");

    /// <summary>Premier démarrage d'une nouvelle version : la page « Nouveautés » s'ouvre d'elle-même, une seule fois.</summary>
    public void AfficherNouveautesSiNouvelle()
    {
        try
        {
            var vue = File.Exists(CheminVersionVue) ? File.ReadAllText(CheminVersionVue).Trim() : null;
            if (!JournalVersions.NouvelleDepuis(vue, VersionActuelle)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(CheminVersionVue)!);
            File.WriteAllText(CheminVersionVue, VersionTexte);
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        OuvrirNouveautes.Execute(null);
    }

    /// <summary>Appelée au démarrage : silencieuse sans réseau ou sans nouveauté.</summary>
    public async Task VerifierMiseAJour()
    {
        _versionDisponible = await MiseAJour.Verifier(Http, VersionActuelle, Installee);
        Signaler(nameof(VersionDisponible));
        Signaler(nameof(MiseAJourVisible));
        Signaler(nameof(TexteMiseAJour));
    }

    async Task Installer()
    {
        // Catalogues ouverts : leurs modifications sont proposées à l'enregistrement avant l'installation, pas pendant la fermeture.
        var catalogues = Application.Current.MainWindow.OwnedWindows.OfType<FenetreCatalogue>().ToList();
        if (_versionDisponible is not { } v || catalogues.Any(c => !c.ConfirmerAbandon()) || !ConfirmerAbandon()) return;
        foreach (var c in catalogues) c.FermetureSansQuestion = true;
        Telechargement = true;
        EtatMiseAJour = "Téléchargement…";
        try
        {
            var dossier = Path.Combine(Path.GetTempPath(), "Dimensionnement-mise-a-jour");
            var fichier = await MiseAJour.Telecharger(Http, v, dossier,
                new Progress<double>(p => { AvancementMiseAJour = p; EtatMiseAJour = $"Téléchargement… {p:P0}"; }));
            EtatMiseAJour = "Installation…";
            if (Installee) LancerInstallateur(fichier);
            else LancerRemplacementPortable(fichier, dossier);
            // Projet déjà enregistré ou abandonné à la confirmation : on ferme sans reposer la question.
            _modifie = false;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Telechargement = false;
            EtatMiseAJour = "";
            MessageBox.Show($"La mise à jour n'a pas pu se faire :\n\n{ex.Message}\n\nTu peux la télécharger à la main sur {MiseAJour.UrlReleases}.",
                "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Installateur Inno Setup en mode silencieux, dans le même mode qu'à l'installation (tous les utilisateurs ou soi seul) ;
    /// /RELANCER=1 lui fait redémarrer l'application une fois les fichiers remplacés.
    /// </summary>
    static void LancerInstallateur(string installateur)
    {
        var pourTous = AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);
        Process.Start(new ProcessStartInfo(installateur,
            $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS {(pourTous ? "/ALLUSERS" : "/CURRENTUSER")} /RELANCER=1")
            { UseShellExecute = true });
    }

    /// <summary>
    /// Version portable : un script attend la fermeture de l'application, décompresse la nouvelle version par-dessus
    /// l'ancienne puis la relance.
    /// </summary>
    static void LancerRemplacementPortable(string archive, string dossier)
    {
        static string Q(string s) => "'" + s.Replace("'", "''") + "'";
        var exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Dimensionnement.App.exe");
        var script = Path.Combine(dossier, "mise-a-jour.ps1");
        File.WriteAllText(script, $"""
            Wait-Process -Id {Environment.ProcessId} -Timeout 60 -ErrorAction SilentlyContinue
            Expand-Archive -LiteralPath {Q(archive)} -DestinationPath {Q(AppContext.BaseDirectory)} -Force
            Start-Process -FilePath {Q(exe)}
            Remove-Item -LiteralPath $PSCommandPath
            """, new UTF8Encoding(true));
        Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            { UseShellExecute = false, CreateNoWindow = true });
    }
}
