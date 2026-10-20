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

    /// <summary>Catalogue des références modifiable par l'utilisateur, à la place du catalogue livré.</summary>
    static readonly string CheminReferences = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueReferences.NomFichier);
    IReadOnlyList<ReferenceCatalogue> _references = CatalogueReferences.Charger(CheminReferences);

    /// <summary>Catalogues des variateurs et des enveloppes modifiables par l'utilisateur.</summary>
    static readonly string CheminVariateurs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueVariateurs.NomFichier);
    static readonly string CheminEnveloppes = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueEnveloppes.NomFichier);
    IReadOnlyList<ModeleVariateur> _variateurs = CatalogueVariateurs.Charger(CheminVariateurs);
    IReadOnlyList<ModeleEnveloppe> _enveloppes = CatalogueEnveloppes.Charger(CheminEnveloppes);

    /// <summary>Catalogues de sécurité et des cartes d'E/S modifiables par l'utilisateur.</summary>
    static readonly string CheminSecurite = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueSecurite.NomFichier);
    static readonly string CheminCartes = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueCartesEs.NomFichier);
    IReadOnlyList<ModeleSecurite> _securite = CatalogueSecurite.Charger(CheminSecurite);
    IReadOnlyList<CarteEs> _cartes = CatalogueCartesEs.Charger(CheminCartes);
    IReadOnlyList<ChoixCpu> _cpus = Array.Empty<ChoixCpu>();

    Projet _projet = new();
    string? _chemin;
    bool _modifie;
    object _selection;

    public ModeleVuePrincipal()
    {
        _selection = _projet;
        _cpus = ListerCpus(_catalogue);
        Resultat = CalculerProjet();

        Nouveau = new Commande(NouveauProjet);
        Ouvrir = new Commande(OuvrirProjet);
        Enregistrer = new Commande(() => EnregistrerProjet(false));
        EnregistrerSous = new Commande(() => EnregistrerProjet(true));
        AjouterMoteur = new Commande(p => AjouterDepart(NouveauMoteur(p is ModeDemarrage m ? m : ModeDemarrage.Direct)));
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
        AjouterStation = new Commande(() => Modifier(() => _projet.Automate.Stations.Add(new StationDeportee
            { Nom = $"Station {_projet.Automate.Stations.Count + 1}", EntreesTor = 16, SortiesTor = 8 })));
        SupprimerStation = new Commande(p => Modifier(() => _projet.Automate.Stations.Remove((StationDeportee)p!)), p => p is StationDeportee);
        AfficherArmoire = new Commande(() => Selection = new PageArmoire(_projet));
        AfficherThermique = new Commande(() => Selection = new PageThermique(_projet));
        AfficherSelectivite = new Commande(() => Selection = new PageSelectivite(_projet));
        AfficherReferences = new Commande(() => Selection = new PageReferences(_projet));
        OuvrirVueArmoire = new Commande(() => Afficher(new FenetreArmoire(this)));
        OuvrirUnifilaire = new Commande(() => Afficher(new FenetreSchema(this, "Schéma unifilaire",
            "Schéma de puissance indicatif, mis à jour à chaque modification du projet. Molette + Ctrl pour zoomer.",
            SchemaUnifilaire.Svg, ExporterUnifilaire!)));
        OuvrirOrganigramme = new Commande(() => Afficher(new FenetreSchema(this, "Organigramme du matériel",
            "De l'arrivée jusqu'à chaque récepteur, départs groupés par rubrique. Survolez un appareil pour son détail ; molette + Ctrl pour zoomer.",
            SchemaOrganigramme.Svg, ExporterOrganigramme!)));
        OuvrirCarte = new Commande(p => OuvrirCarteMateriel((CarteMateriel)p!), p => p is CarteMateriel);
        DeplacerAppareil = new Commande(p => Modifier(() =>
        {
            var d = (DeplacementAppareil)p!;
            _projet.Armoire.Positions[d.Repere] = new PositionManuelle { X = d.X, Y = d.Y };
        }), p => p is DeplacementAppareil);
        ReinitialiserImplantation = new Commande(() => Modifier(() =>
            {
                _projet.Armoire.Positions.Clear();
                _projet.Armoire.Rails.Clear();
                _projet.Armoire.Goulottes.Clear();
            }),
            () => _projet.Armoire.Positions.Count > 0 || _projet.Armoire.Rails.Count > 0 || _projet.Armoire.Goulottes.Count > 0);
        AjouterRail = new Commande(() => Modifier(() =>
        {
            var (x, y, longueur) = PositionNouvelle();
            _projet.Armoire.Rails.Add(new RailManuel { X = x, Y = y, Longueur = longueur });
        }));
        AjouterGoulotte = new Commande(p => Modifier(() =>
        {
            var (x, y, longueur) = PositionNouvelle();
            var verticale = p as string == "V";
            _projet.Armoire.Goulottes.Add(verticale
                ? new GoulotteManuelle { X = x + longueur / 2, Y = y - longueur / 2, Longueur = longueur, Verticale = true }
                : new GoulotteManuelle { X = x, Y = y + 60, Longueur = longueur });
        }));
        ModifierArmoire = new Commande(p => Modifier(() => AppliquerAction((ActionArmoire)p!)), p => p is ActionArmoire);
        ExporterDevis = new Commande(EnregistrerDevis);
        ExporterUnifilaire = new Commande(() => EnregistrerSvg("unifilaire", SchemaUnifilaire.Svg(Resultat), "Impossible d'enregistrer le schéma unifilaire"));
        ExporterOrganigramme = new Commande(() => EnregistrerSvg("organigramme", SchemaOrganigramme.Svg(Resultat), "Impossible d'enregistrer l'organigramme"));
        OuvrirCatalogueReferences = new Commande(() => OuvrirCatalogueUtilisateur(CheminReferences, CatalogueReferences.TexteEmbarque(),
            "Impossible d'ouvrir le catalogue des références"));
        RechargerCatalogueReferences = new Commande(() =>
        {
            _references = CatalogueReferences.Charger(CheminReferences);
            _variateurs = CatalogueVariateurs.Charger(CheminVariateurs);
            _enveloppes = CatalogueEnveloppes.Charger(CheminEnveloppes);
            _securite = CatalogueSecurite.Charger(CheminSecurite);
            Recalculer();
        });
        OuvrirCatalogueVariateurs = new Commande(() => OuvrirCatalogueUtilisateur(CheminVariateurs, CatalogueVariateurs.TexteEmbarque(),
            "Impossible d'ouvrir le catalogue des variateurs"));
        OuvrirCatalogueEnveloppes = new Commande(() => OuvrirCatalogueUtilisateur(CheminEnveloppes, CatalogueEnveloppes.TexteEmbarque(),
            "Impossible d'ouvrir le catalogue des enveloppes"));
        OuvrirCatalogueSecurite = new Commande(() => OuvrirCatalogueUtilisateur(CheminSecurite, CatalogueSecurite.TexteEmbarque(),
            "Impossible d'ouvrir le catalogue de sécurité"));
        OuvrirCatalogueCartes = new Commande(() => OuvrirCatalogueUtilisateur(CheminCartes, CatalogueCartesEs.TexteEmbarque(),
            "Impossible d'ouvrir le catalogue des cartes d'E/S"));
        OuvrirCatalogue = new Commande(OuvrirCatalogueAutomates);
        RechargerCatalogue = new Commande(() =>
        {
            _catalogue = CatalogueAutomates.Charger(CheminCatalogue);
            _cartes = CatalogueCartesEs.Charger(CheminCartes);
            _cpus = ListerCpus(_catalogue);
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
    public Commande AjouterStation { get; }
    public Commande SupprimerStation { get; }
    public Commande AfficherArmoire { get; }
    public Commande AfficherThermique { get; }
    public Commande AfficherSelectivite { get; }
    public Commande AfficherReferences { get; }
    public Commande OuvrirVueArmoire { get; }
    public Commande OuvrirUnifilaire { get; }
    public Commande OuvrirOrganigramme { get; }
    public Commande OuvrirCarte { get; }
    public Commande DeplacerAppareil { get; }
    public Commande ReinitialiserImplantation { get; }
    public Commande AjouterRail { get; }
    public Commande AjouterGoulotte { get; }
    public Commande ModifierArmoire { get; }

    /// <summary>Position d'un rail ou d'une goulotte ajouté : au milieu de la plaque de la première cellule.</summary>
    (double X, double Y, double Longueur) PositionNouvelle()
    {
        var a = Resultat.Armoire;
        if (a is null) return (100, 300, 400);
        var longueur = Math.Max(100, Math.Min(400, a.PlaqueLargeur - 2 * a.Laterale));
        return (Math.Round(a.PlaqueX + a.Laterale), Math.Round(a.Hauteur / 2), longueur);
    }

    void AppliquerAction(ActionArmoire action)
    {
        var config = _projet.Armoire;
        if (config.Rails.FirstOrDefault(r => r.Id == action.Id) is { } rail)
            switch (action.Action)
            {
                case "Deplacer": rail.X = action.X; rail.Y = action.Y; break;
                case "Longueur": rail.Longueur = Math.Max(50, rail.Longueur + action.X); break;
                case "Supprimer": config.Rails.Remove(rail); break;
            }
        if (config.Goulottes.FirstOrDefault(g => g.Id == action.Id) is { } goulotte)
            switch (action.Action)
            {
                case "Deplacer": goulotte.X = action.X; goulotte.Y = action.Y; break;
                case "Longueur": goulotte.Longueur = Math.Max(50, goulotte.Longueur + action.X); break;
                case "Modele": goulotte.Largeur = action.X; goulotte.Hauteur = action.Y; break;
                case "Supprimer": config.Goulottes.Remove(goulotte); break;
            }
    }
    public Commande ExporterDevis { get; }
    public Commande ExporterUnifilaire { get; }
    public Commande ExporterOrganigramme { get; }
    public Commande OuvrirCatalogueReferences { get; }
    public Commande RechargerCatalogueReferences { get; }
    public Commande OuvrirCatalogueVariateurs { get; }
    public Commande OuvrirCatalogueEnveloppes { get; }
    public Commande OuvrirCatalogue { get; }
    public Commande OuvrirCatalogueSecurite { get; }
    public Commande OuvrirCatalogueCartes { get; }

    /// <summary>CPU proposées au choix manuel, précédées du choix automatique.</summary>
    public IReadOnlyList<ChoixCpu> CpusDisponibles => _cpus;

    static IReadOnlyList<ChoixCpu> ListerCpus(IReadOnlyList<CpuAutomate> catalogue) =>
        catalogue.Select(c => new ChoixCpu(c.Reference, c.Libelle))
            .Prepend(new ChoixCpu("", "Automatique, selon la charge et les cartes"))
            .ToList();

    ResultatProjet CalculerProjet() =>
        CalculProjet.Calculer(_projet, _catalogue, _references, _variateurs, _enveloppes, _securite, _cartes);
    public Commande RechargerCatalogue { get; }
    public Commande Forcer { get; }
    public Commande AnnulerForcage { get; }

    public ObservableCollection<string> Recents { get; }

    public Projet Projet => _projet;
    public ResultatProjet Resultat { get; private set; }

    /// <summary>Matériel présenté en cartes : l'arrivée, les départs par rubrique, puis l'automatisme et la sécurité.</summary>
    public IReadOnlyList<GroupeMateriel> Groupes => SyntheseMateriel.Grouper(Resultat);

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

    /// <summary>Appareils commandables avec leur référence, pour la page des références.</summary>
    public IReadOnlyList<LigneReference> LignesReferences => Resultat.Arbre.Parcourir()
        .Where(CatalogueReferences.Commandable)
        .Select(e => new LigneReference(e, _projet.References))
        .ToList();

    /// <summary>Résumé de l'armoire et de sa thermique, pour les bandeaux.</summary>
    public string ResumeArmoire => Resultat.Armoire is { } a
        ? $"{a.Type} {a.Marque} {a.Gamme} {a.Largeur:0} × {a.Hauteur:0} × {a.Profondeur:0} mm{(a.Cellules > 1 ? $", {a.Cellules} cellules" : "")}" +
          $"{(a.TailleImposee ? " (imposée)" : "")} – " +
          $"plaque occupée à {a.Occupation:0} % – {Resultat.Thermique?.PuissanceDissipee:0} W dissipés"
        : "";

    public string ResumeThermique => Resultat.Thermique switch
    {
        null => "",
        { Solution: Core.Armoire.SolutionThermique.Ventilation } t => $"Ventilateur à filtre de {t.Ventilateur:0} m³/h (besoin {t.DebitNecessaire:0} m³/h)",
        { Solution: Core.Armoire.SolutionThermique.Climatisation } t => $"Climatiseur de {t.Climatiseur:0} W (besoin {t.PuissanceFrigorifique:0} W)",
        var t => $"Convection naturelle suffisante ({t.TemperatureInterieure:0.0} °C à l'intérieur)",
    };

    public NoteDeCalcul Note => Selection switch
    {
        PageArmoire => Resultat.Armoire?.Note ?? Resultat.NoteArrivee,
        PageThermique => Resultat.Thermique?.Note ?? Resultat.NoteArrivee,
        PageSelectivite => Resultat.Selectivite?.Note ?? Resultat.NoteArrivee,
        PageReferences => Resultat.References?.Note ?? Resultat.NoteArrivee,
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
        PageArmoire => "NOTE DE CALCUL – ARMOIRE ET IMPLANTATION",
        PageThermique => "NOTE DE CALCUL – BILAN THERMIQUE",
        PageSelectivite => "NOTE DE CALCUL – SÉLECTIVITÉ",
        PageReferences => "RÉFÉRENCES DU MATÉRIEL",
        _ => "NOTE DE CALCUL – ARRIVÉE ET BILAN DE PUISSANCE",
    };

    public string Titre => $"Dimensionnement électrique – {_projet.Nom}{(_modifie ? " *" : "")}";
    public string Chemin => _chemin ?? "Projet non enregistré";
    public bool Modifie => _modifie;

    /// <summary>Recalcule tout le projet après une modification.</summary>
    public void Recalculer()
    {
        _modifie = true;
        Resultat = CalculerProjet();
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

    Dictionary<string, Forcage> Forcages() => Selection switch
    {
        Depart d => d.Forcages,
        PageArmoire or PageThermique => _projet.Armoire.Forcages,
        _ => _projet.ForcagesArrivee,
    };

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
        Resultat = CalculerProjet();
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

    void OuvrirCatalogueAutomates() =>
        OuvrirCatalogueUtilisateur(CheminCatalogue, CatalogueAutomates.TexteEmbarque(), "Impossible d'ouvrir le catalogue des automates");

    /// <summary>Copie le catalogue livré dans le dossier de l'utilisateur à la première ouverture, puis l'ouvre dans le Bloc-notes.</summary>
    static void OuvrirCatalogueUtilisateur(string chemin, string contenuInitial, string erreur)
    {
        try
        {
            if (!File.Exists(chemin))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(chemin)!);
                File.WriteAllText(chemin, contenuInitial);
            }
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{chemin}\"") { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Erreur(erreur, ex);
        }
    }

    void EnregistrerDevis()
    {
        var chemin = DemanderChemin("Classeur Excel (*.xlsx)|*.xlsx", $"{NomFichier(_projet.Nom)}-demande-de-devis-{NomFichier(_projet.Indice)}.xlsx");
        if (chemin is null) return;
        try
        {
            ExportExcel.EnregistrerDevis(Resultat, chemin);
            AfficherFichier(chemin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Erreur("Impossible d'enregistrer la demande de devis", ex);
        }
    }

    void EnregistrerSvg(string suffixe, string svg, string titreErreur)
    {
        var chemin = DemanderChemin("Image vectorielle (*.svg)|*.svg", $"{NomFichier(_projet.Nom)}-{suffixe}-{NomFichier(_projet.Indice)}.svg");
        if (chemin is null) return;
        try
        {
            File.WriteAllText(chemin, svg);
            AfficherFichier(chemin);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Erreur(titreErreur, ex);
        }
    }

    static DepartMoteur NouveauMoteur(ModeDemarrage demarrage) => new()
    {
        Designation = demarrage switch
        {
            ModeDemarrage.Variateur => "Moteur sur variateur",
            ModeDemarrage.DemarreurProgressif => "Moteur sur démarreur progressif",
            ModeDemarrage.Inverseur => "Moteur deux sens",
            ModeDemarrage.EtoileTriangle => "Moteur étoile-triangle",
            _ => "Moteur",
        },
        PuissanceKw = 5.5, CosPhi = 0.84, Rendement = 0.88, Demarrage = demarrage,
    };

    /// <summary>Une carte ouvre son départ ; l'arrivée ouvre le projet, la commande la page automate ou sécurité.</summary>
    void OuvrirCarteMateriel(CarteMateriel carte)
    {
        if (carte.Depart is { } d)
        {
            Selection = _projet.Departs.FirstOrDefault(x => x.Id == d.Id) ?? (object)_projet;
            return;
        }
        Selection = carte.Chaine.FirstOrDefault()?.Element.Famille switch
        {
            "Automate" or "Entrées/sorties" or "Accessoire automate" => _projet.Automate,
            "Sécurité" => _projet.Securite,
            _ => _projet,
        };
    }

    /// <summary>Ouvre une fenêtre secondaire non modale ; elle suit les recalculs du projet.</summary>
    static void Afficher(Window fenetre)
    {
        fenetre.Owner = Application.Current.MainWindow;
        fenetre.Show();
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

/// <summary>Une CPU proposée dans la liste de choix ; référence vide pour le choix automatique.</summary>
public record ChoixCpu(string Reference, string Libelle);
