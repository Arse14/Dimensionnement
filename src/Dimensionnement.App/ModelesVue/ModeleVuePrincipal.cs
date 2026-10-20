using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.App.Vues;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using Dimensionnement.Core.Persistance;
using Dimensionnement.Core.Rapport;
using Microsoft.Win32;

namespace Dimensionnement.App.ModelesVue;

public class ModeleVuePrincipal : Observable
{
    const string Filtre = "Projet de dimensionnement (*.dimelec)|*.dimelec";
    readonly ProjetsRecents _recents = new();

    /// <summary>Catalogue des CPU modifiable par l'utilisateur, à la place du catalogue livré.</summary>
    static readonly string CheminCatalogue = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueAutomates.NomFichier);
    IReadOnlyList<CpuAutomate> _catalogue = CatalogueAutomates.Charger(CheminCatalogue);

    Projet _projet = new();
    string? _chemin;
    bool _modifie;
    object _selection;

    public ModeleVuePrincipal()
    {
        _selection = _projet;
        Resultat = CalculProjet.Calculer(_projet, _catalogue);

        Nouveau = new Commande(NouveauProjet);
        Ouvrir = new Commande(OuvrirProjet);
        Enregistrer = new Commande(() => EnregistrerProjet(false));
        EnregistrerSous = new Commande(() => EnregistrerProjet(true));
        AjouterMoteur = new Commande(() => AjouterDepart(new DepartMoteur
            { Designation = "Moteur", PuissanceKw = 5.5, CosPhi = 0.84, Rendement = 0.88 }));
        AjouterResistif = new Commande(() => AjouterDepart(new DepartResistif { Designation = "Résistance", PuissanceKw = 3 }));
        AjouterPrise = new Commande(() => AjouterDepart(new DepartPrise { Designation = "Prise de courant", Rubrique = "Auxiliaires" }));
        AjouterGenerique = new Commande(() => AjouterDepart(new DepartGenerique
            { Designation = "Charge", PuissanceKw = 1, Rubrique = "Auxiliaires", Alimentation = TypeAlimentation.Mono230 }));
        AjouterAlimentation = new Commande(() => AjouterDepart(new DepartAlimentation24V()));
        Supprimer = new Commande(SupprimerDepart, () => Selection is Depart);
        Rapport = new Commande(GenererRapport);
        ExporterExcel = new Commande(EnregistrerExcel);
        ExporterSistema = new Commande(EnregistrerSistema, () => _projet.Securite.Fonctions.Count > 0);
        AfficherProjet = new Commande(() => Selection = _projet);
        AfficherSecurite = new Commande(() => Selection = _projet.Securite);
        AfficherAutomate = new Commande(() => Selection = _projet.Automate);
        AfficherBilan24V = new Commande(() => Selection = new PageBilan24V(_projet));
        AfficherControles = new Commande(() => Selection = new PageControles(_projet));
        AjouterConsommateur = new Commande(() => Modifier(() => _projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Consommateur", CourantMa = 100 })));
        SupprimerConsommateur = new Commande(p => Modifier(() => _projet.Consommateurs24V.Remove((Consommateur24V)p!)), p => p is Consommateur24V);
        AjouterFonction = new Commande(() => Modifier(() => _projet.Securite.Fonctions.Add(new FonctionSecurite
            { Nom = $"Fonction {_projet.Securite.Fonctions.Count + 1}" })));
        SupprimerFonction = new Commande(p => Modifier(() => _projet.Securite.Fonctions.Remove((FonctionSecurite)p!)), p => p is FonctionSecurite);
        OuvrirCatalogue = new Commande(OuvrirCatalogueAutomates);
        RechargerCatalogue = new Commande(() =>
        {
            _catalogue = CatalogueAutomates.Charger(CheminCatalogue);
            Recalculer();
        });
        Forcer = new Commande(p => ForcerValeur(p as LigneCalcul), p => p is LigneCalcul { Forcable: true });
        AnnulerForcage = new Commande(p => AnnulerValeur(p as LigneCalcul), p => p is LigneCalcul { Forcage: not null });
        Recents = new ObservableCollection<string>(_recents.Lire());
    }

    public Commande Nouveau { get; }
    public Commande Ouvrir { get; }
    public Commande Enregistrer { get; }
    public Commande EnregistrerSous { get; }
    public Commande AjouterMoteur { get; }
    public Commande AjouterResistif { get; }
    public Commande AjouterPrise { get; }
    public Commande AjouterGenerique { get; }
    public Commande AjouterAlimentation { get; }
    public Commande Supprimer { get; }
    public Commande Rapport { get; }
    public Commande ExporterExcel { get; }
    public Commande ExporterSistema { get; }
    public Commande AfficherProjet { get; }
    public Commande AfficherSecurite { get; }
    public Commande AfficherAutomate { get; }
    public Commande AfficherBilan24V { get; }
    public Commande AfficherControles { get; }
    public Commande AjouterConsommateur { get; }
    public Commande SupprimerConsommateur { get; }
    public Commande AjouterFonction { get; }
    public Commande SupprimerFonction { get; }
    public Commande OuvrirCatalogue { get; }
    public Commande RechargerCatalogue { get; }
    public Commande Forcer { get; }
    public Commande AnnulerForcage { get; }

    public ObservableCollection<string> Recents { get; }

    public Projet Projet => _projet;
    public ResultatProjet Resultat { get; private set; }

    /// <summary>Racine de l'arbre du matériel, sous forme de collection pour le TreeView.</summary>
    public IEnumerable<ElementMateriel> Arbre => new[] { Resultat.Arbre };

    /// <summary>Élément en cours d'édition : le projet (et son réseau) ou un départ.</summary>
    public object Selection
    {
        get => _selection;
        set
        {
            if (ReferenceEquals(_selection, value)) return;
            _selection = value;
            Signaler();
            Signaler(nameof(Note));
            Signaler(nameof(TitreNote));
        }
    }

    public NoteDeCalcul Note => Selection switch
    {
        Depart d => Resultat.Departs.FirstOrDefault(r => r.Depart == d)?.Note ?? Resultat.NoteArrivee,
        ConfigurationSecurite => Resultat.Securite?.Note ?? Resultat.NoteArrivee,
        ConfigurationAutomate => Resultat.Automate?.Note ?? Resultat.NoteArrivee,
        _ => Resultat.NoteArrivee,
    };

    public string TitreNote => Selection switch
    {
        Depart d => $"NOTE DE CALCUL – D{d.Numero:00} {d.Designation}".ToUpperInvariant(),
        ConfigurationSecurite => "NOTE DE CALCUL – SÉCURITÉ MACHINE",
        ConfigurationAutomate => "NOTE DE CALCUL – AUTOMATE",
        _ => "NOTE DE CALCUL – ARRIVÉE ET BILAN DE PUISSANCE",
    };

    public string Titre => $"Dimensionnement électrique – {_projet.Nom}{(_modifie ? " *" : "")}";
    public string Chemin => _chemin ?? "Projet non enregistré";
    public bool Modifie => _modifie;

    /// <summary>Recalcule tout le projet après une modification.</summary>
    public void Recalculer()
    {
        _modifie = true;
        Resultat = CalculProjet.Calculer(_projet, _catalogue);
        SignalerTout();
    }

    void Modifier(Action action)
    {
        action();
        Recalculer();
    }

    void AjouterDepart(Depart depart)
    {
        depart.Alimentation = depart is DepartPrise or DepartGenerique or DepartAlimentation24V
            ? depart.Alimentation
            : _projet.Reseau.Alimentation == TypeAlimentation.Mono230 ? TypeAlimentation.Mono230 : TypeAlimentation.Tri400;
        _projet.Ajouter(depart);
        _selection = depart;
        Recalculer();
    }

    void SupprimerDepart()
    {
        if (Selection is not Depart d) return;
        if (MessageBox.Show($"Supprimer le départ D{d.Numero:00} « {d.Designation} » ?", "Supprimer un départ",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _projet.Departs.Remove(d);
        _selection = _projet;
        Recalculer();
    }

    void ForcerValeur(LigneCalcul? ligne)
    {
        if (ligne is null) return;
        var fenetre = new FenetreForcage(ligne) { Owner = Application.Current.MainWindow };
        if (fenetre.ShowDialog() != true) return;
        Forcages()[ligne.Code] = new Forcage
        {
            Valeur = fenetre.Valeur, Motif = fenetre.Motif,
            Auteur = _projet.Auteur is { Length: > 0 } a ? a : Environment.UserName,
        };
        Recalculer();
    }

    void AnnulerValeur(LigneCalcul? ligne)
    {
        if (ligne is null) return;
        Forcages().Remove(ligne.Code);
        Recalculer();
    }

    Dictionary<string, Forcage> Forcages() => Selection is Depart d ? d.Forcages : _projet.ForcagesArrivee;

    /// <summary>Demande confirmation avant d'abandonner des modifications non enregistrées.</summary>
    public bool ConfirmerAbandon()
    {
        if (!_modifie) return true;
        var reponse = MessageBox.Show("Le projet a été modifié. Enregistrer les modifications ?", "Dimensionnement électrique",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return reponse switch
        {
            MessageBoxResult.Yes => EnregistrerProjet(false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    void NouveauProjet()
    {
        if (!ConfirmerAbandon()) return;
        Charger(new Projet(), null);
    }

    void OuvrirProjet()
    {
        if (!ConfirmerAbandon()) return;
        var dialogue = new OpenFileDialog { Filter = Filtre };
        if (dialogue.ShowDialog() == true) OuvrirChemin(dialogue.FileName, false);
    }

    public void OuvrirChemin(string chemin, bool confirmer = true)
    {
        if (confirmer && !ConfirmerAbandon()) return;
        try
        {
            Charger(FichierProjet.Ouvrir(chemin), chemin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                                       or System.Text.Json.JsonException)
        {
            MessageBox.Show($"Impossible d'ouvrir le projet :\n{ex.Message}", "Ouvrir un projet",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void Charger(Projet projet, string? chemin)
    {
        _projet = projet;
        _chemin = chemin;
        _selection = projet;
        if (chemin is not null) MemoriserRecent(chemin);
        Resultat = CalculProjet.Calculer(_projet, _catalogue);
        _modifie = false;
        SignalerTout();
    }

    bool EnregistrerProjet(bool demanderChemin)
    {
        var chemin = _chemin;
        if (demanderChemin || chemin is null)
        {
            var dialogue = new SaveFileDialog { Filter = Filtre, FileName = NomFichier(_projet.Affaire is { Length: > 0 } a ? a : _projet.Nom) };
            if (dialogue.ShowDialog() != true) return false;
            chemin = dialogue.FileName;
        }
        try
        {
            FichierProjet.Enregistrer(_projet, chemin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"Impossible d'enregistrer le projet :\n{ex.Message}", "Enregistrer",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        _chemin = chemin;
        _modifie = false;
        MemoriserRecent(chemin);
        SignalerTout();
        return true;
    }

    void MemoriserRecent(string chemin)
    {
        _recents.Ajouter(chemin);
        Recents.Clear();
        foreach (var r in _recents.Lire()) Recents.Add(r);
    }

    async void GenererRapport()
    {
        var chemin = DemanderChemin("Rapport PDF (*.pdf)|*.pdf", $"{NomFichier(_projet.Nom)}-rapport-{NomFichier(_projet.Indice)}.pdf");
        if (chemin is null) return;

        // Chaque émission du rapport est tracée dans l'historique des révisions.
        if (_projet.Revisions.LastOrDefault()?.Indice != _projet.Indice)
        {
            _projet.Revisions.Add(new Revision
            {
                Indice = _projet.Indice, Date = DateTime.Now,
                Auteur = _projet.Auteur is { Length: > 0 } a ? a : Environment.UserName,
                Description = "Émission du rapport de dimensionnement",
            });
            Recalculer();
        }

        var html = RapportHtml.Generer(Resultat);
        try
        {
            await GenerateurPdf.Generer(html, chemin);
            AfficherFichier(chemin);
        }
        catch (Exception ex)
        {
            // Sans moteur WebView2, le rapport reste disponible en HTML, imprimable en PDF depuis le navigateur.
            var cheminHtml = Path.ChangeExtension(chemin, ".html");
            try
            {
                File.WriteAllText(cheminHtml, html);
                AfficherFichier(cheminHtml);
                MessageBox.Show($"Le PDF n'a pas pu être généré ({ex.Message}).\nLe rapport a été enregistré en HTML : " +
                                "imprimez-le en PDF depuis le navigateur.", "Rapport", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
            {
                Erreur("Impossible de générer le rapport", ex2);
            }
        }
    }

    void EnregistrerExcel()
    {
        var chemin = DemanderChemin("Classeur Excel (*.xlsx)|*.xlsx", $"{NomFichier(_projet.Nom)}-nomenclature-{NomFichier(_projet.Indice)}.xlsx");
        if (chemin is null) return;
        try
        {
            ExportExcel.Enregistrer(Resultat, chemin);
            AfficherFichier(chemin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Erreur("Impossible d'enregistrer le classeur Excel", ex);
        }
    }

    void EnregistrerSistema()
    {
        var chemin = DemanderChemin("Fichier CSV (*.csv)|*.csv", $"{NomFichier(_projet.Nom)}-fonctions-securite.csv");
        if (chemin is null) return;
        try
        {
            // UTF-8 avec BOM pour qu'Excel affiche correctement les accents.
            File.WriteAllText(chemin, ExportSistema.Generer(Resultat), new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Erreur("Impossible d'enregistrer l'export SISTEMA", ex);
        }
    }

    void OuvrirCatalogueAutomates()
    {
        try
        {
            if (!File.Exists(CheminCatalogue))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CheminCatalogue)!);
                File.WriteAllText(CheminCatalogue, CatalogueAutomates.TexteEmbarque());
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{CheminCatalogue}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Erreur("Impossible d'ouvrir le catalogue des automates", ex);
        }
    }

    string? DemanderChemin(string filtre, string nom)
    {
        var dialogue = new SaveFileDialog
        {
            Filter = filtre, FileName = nom,
            InitialDirectory = _chemin is not null ? Path.GetDirectoryName(_chemin) : null,
        };
        return dialogue.ShowDialog() == true ? dialogue.FileName : null;
    }

    static void AfficherFichier(string chemin)
    {
        try
        {
            Process.Start(new ProcessStartInfo(chemin) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Aucun programme associé : le fichier est tout de même enregistré.
        }
    }

    static void Erreur(string titre, Exception ex) =>
        MessageBox.Show($"{titre} :\n{ex.Message}", "Dimensionnement électrique", MessageBoxButton.OK, MessageBoxImage.Error);

    static string NomFichier(string texte)
    {
        var invalides = Path.GetInvalidFileNameChars();
        var propre = new string(texte.Select(c => invalides.Contains(c) ? '_' : c).ToArray()).Trim();
        return propre.Length > 0 ? propre : "projet";
    }
}
