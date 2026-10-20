using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Dimensionnement.App.Infrastructure;
using Dimensionnement.App.Vues;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Persistance;
using Dimensionnement.Core.Rapport;
using Microsoft.Win32;

namespace Dimensionnement.App.ModelesVue;

public class ModeleVuePrincipal : Observable
{
    const string Filtre = "Projet de dimensionnement (*.dimelec)|*.dimelec";
    readonly ProjetsRecents _recents = new();

    Projet _projet = new();
    string? _chemin;
    bool _modifie;
    object _selection;

    public ModeleVuePrincipal()
    {
        _selection = _projet;
        Resultat = CalculProjet.Calculer(_projet);

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
        Supprimer = new Commande(SupprimerDepart, () => Selection is Depart);
        Rapport = new Commande(GenererRapport);
        AfficherProjet = new Commande(() => Selection = _projet);
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
    public Commande Supprimer { get; }
    public Commande Rapport { get; }
    public Commande AfficherProjet { get; }
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

    public NoteDeCalcul Note => Selection is Depart d
        ? Resultat.Departs.FirstOrDefault(r => r.Depart == d)?.Note ?? Resultat.NoteArrivee
        : Resultat.NoteArrivee;

    public string TitreNote => Selection is Depart d
        ? $"NOTE DE CALCUL – D{d.Numero:00} {d.Designation}".ToUpperInvariant()
        : "NOTE DE CALCUL – ARRIVÉE ET BILAN DE PUISSANCE";

    public string Titre => $"Dimensionnement électrique – {_projet.Nom}{(_modifie ? " *" : "")}";
    public string Chemin => _chemin ?? "Projet non enregistré";
    public bool Modifie => _modifie;

    /// <summary>Recalcule tout le projet après une modification.</summary>
    public void Recalculer()
    {
        _modifie = true;
        Resultat = CalculProjet.Calculer(_projet);
        SignalerTout();
    }

    void AjouterDepart(Depart depart)
    {
        depart.Alimentation = depart is DepartPrise or DepartGenerique
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
        Resultat = CalculProjet.Calculer(_projet);
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

    void GenererRapport()
    {
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

        var dossier = _chemin is not null
            ? Path.GetDirectoryName(_chemin)!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var fichier = Path.Combine(dossier, $"{NomFichier(_projet.Nom)}-rapport-{NomFichier(_projet.Indice)}.html");
        try
        {
            File.WriteAllText(fichier, RapportHtml.Generer(Resultat));
            Process.Start(new ProcessStartInfo(fichier) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show($"Impossible de générer le rapport :\n{ex.Message}", "Rapport",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    static string NomFichier(string texte)
    {
        var invalides = Path.GetInvalidFileNameChars();
        var propre = new string(texte.Select(c => invalides.Contains(c) ? '_' : c).ToArray()).Trim();
        return propre.Length > 0 ? propre : "projet";
    }
}
