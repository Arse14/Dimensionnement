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

public partial class ModeleVuePrincipal : Observable
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
    static readonly string CheminCapteurs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueCapteursSecurite.NomFichier);
    static readonly string CheminEncombrements = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueEncombrements.NomFichier);
    static readonly string CheminRepartiteurs = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueRepartiteurs.NomFichier);
    IReadOnlyList<ModeleCapteurSecurite> _capteurs = CatalogueCapteursSecurite.Charger(CheminCapteurs);
    IReadOnlyList<CarteEs> _cartes = CatalogueCartesEs.Charger(CheminCartes);
    IReadOnlyList<ChoixCpu> _cpus = Array.Empty<ChoixCpu>();

    /// <summary>Catalogues des alimentations / transformateurs et des IHM modifiables par l'utilisateur.</summary>
    static readonly string CheminAlimentations = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueAlimentations.NomFichier);
    static readonly string CheminIhm = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", CatalogueIhm.NomFichier);
    IReadOnlyList<ModeleAlimentation> _alimentations = CatalogueAlimentations.Charger(CheminAlimentations);
    IReadOnlyList<ModeleIhm> _ihm = CatalogueIhm.Charger(CheminIhm);

    /// <summary>Tensions dont l'utilisateur a refusé ou supprimé la source ajoutée automatiquement, jusqu'au prochain projet.</summary>
    readonly HashSet<TensionCommande> _sourcesIgnorees = new();
    bool _noteVisible;

    Projet _projet = new();
    string? _chemin;
    bool _modifie;
    object _selection;

    public ModeleVuePrincipal()
    {
        _selection = _projet;
        CatalogueEncombrements.Actif = CatalogueEncombrements.Charger(CheminEncombrements);
        CatalogueRepartiteurs.Actif = CatalogueRepartiteurs.Charger(CheminRepartiteurs);
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
        AjouterMoteurFrein = new Commande(() =>
        {
            var m = NouveauMoteur(ModeDemarrage.Direct);
            m.Designation = "Moteur-frein";
            m.Frein = true;
            AjouterDepart(m);
        });
        AjouterCoffret = new Commande(() => AjouterDepart(new DepartCoffret
            { Designation = "Coffret déporté", PuissanceKw = 10, CosPhi = 0.85, Rubrique = "Puissance", Alimentation = TypeAlimentation.Tetra400 }));
        AjouterEclairage = new Commande(() => AjouterDepart(new DepartGenerique
            { Designation = "Éclairage machine", PuissanceKw = 0.2, CosPhi = 0.95, Rubrique = "Auxiliaires", Alimentation = TypeAlimentation.Mono230 }));
        AjouterPeripherique = new Commande(() => Modifier(() => _projet.PeripheriquesMachine.Add(
            new PeripheriqueMachine { Nom = $"Îlot {_projet.PeripheriquesMachine.Count + 1}" })));
        SupprimerPeripherique = new Commande(p => Modifier(() => _projet.PeripheriquesMachine.Remove((PeripheriqueMachine)p!)), p => p is PeripheriqueMachine);
        RenommerElement = new Commande(p => Renommer((ElementMateriel)p!), p => p is ElementMateriel);
        AjouterAlimentation = new Commande(() => AjouterDepart(new DepartAlimentation24V()));
        AjouterTransformateur = new Commande(p => AjouterDepart(NouveauTransformateur(p is TensionCommande t ? t : TensionCommande.Alternatif24)));
        BasculerNote = new Commande(() => NoteVisible = !NoteVisible);
        AfficherBilanPuissance = new Commande(() => Selection = new PageBilanPuissance(_projet));
        AjouterPupitre = new Commande(() => Modifier(() => _projet.Automate.Pupitres.Add(new PupitreIhm { Nom = $"IHM {_projet.Automate.Pupitres.Count + 1}" })));
        SupprimerPupitre = new Commande(p => Modifier(() => _projet.Automate.Pupitres.Remove((PupitreIhm)p!)), p => p is PupitreIhm);
        ApercuDevis = new Commande(() => Afficher(new FenetreApercuDevis(this)));
        ApercuRapport = new Commande(() => Afficher(new FenetreApercuRapport(this)));
        OuvrirCatalogueAlimentations = new Commande(() => OuvrirNavigateur("alimentations"));
        OuvrirCatalogueIhm = new Commande(() => OuvrirNavigateur("ihm"));
        OuvrirCatalogues = new Commande(p => OuvrirNavigateur(p as string ?? "alimentations"));
        Supprimer = new Commande(SupprimerDepart, () => Selection is Depart);
        Rapport = new Commande(GenererRapport);
        ExporterExcel = new Commande(EnregistrerExcel);
        ExporterSistema = new Commande(EnregistrerSistema, () => _projet.Securite.Fonctions.Count > 0);
        AfficherProjet = new Commande(() => Selection = _projet);
        AfficherSecurite = new Commande(() => Selection = _projet.Securite);
        AfficherAutomate = new Commande(() => Selection = _projet.Automate);
        AfficherBilan24V = new Commande(() => Selection = new PageBilan24V(_projet));
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
        OuvrirOrganigramme = new Commande(() => Afficher(new FenetreSchema(this, "Organigramme du matériel",
            "De l'arrivée jusqu'à chaque récepteur, départs groupés par rubrique. Survolez un appareil pour son détail ; molette + Ctrl pour zoomer.",
            SchemaOrganigramme.Svg, ExporterOrganigramme!)));
        OuvrirCarte = new Commande(p => OuvrirCarteMateriel((CarteMateriel)p!), p => p is CarteMateriel);
        DeplacerAppareil = new Commande(p => Modifier(() =>
        {
            var d = (DeplacementAppareil)p!;
            ConfigCourante.Positions[d.Repere] = new PositionManuelle { X = d.X, Y = d.Y };
        }), p => p is DeplacementAppareil);
        ReinitialiserImplantation = new Commande(() => Modifier(() =>
            {
                ConfigCourante.Positions.Clear();
                ConfigCourante.Rails.Clear();
                ConfigCourante.Goulottes.Clear();
            }),
            () => ConfigCourante.Positions.Count > 0 || ConfigCourante.Rails.Count > 0 || ConfigCourante.Goulottes.Count > 0);
        AjouterRail = new Commande(() => Modifier(() =>
        {
            var (x, y, longueur) = PositionNouvelle();
            ConfigCourante.Rails.Add(new RailManuel { X = x, Y = y, Longueur = longueur });
        }));
        AjouterGoulotte = new Commande(p => Modifier(() =>
        {
            var (x, y, longueur) = PositionNouvelle();
            var verticale = p as string == "V";
            ConfigCourante.Goulottes.Add(verticale
                ? new GoulotteManuelle { X = x + longueur / 2, Y = y - longueur / 2, Longueur = longueur, Verticale = true }
                : new GoulotteManuelle { X = x, Y = y + 60, Longueur = longueur });
        }));
        ModifierArmoire = new Commande(p => Modifier(() => AppliquerAction((ActionArmoire)p!)), p => p is ActionArmoire);
        ExporterDevis = new Commande(EnregistrerDevis);
        ExporterOrganigramme = new Commande(() => EnregistrerSvg("organigramme", SchemaOrganigramme.Svg(Resultat), "Impossible d'enregistrer l'organigramme"));
        OuvrirCatalogueReferences = new Commande(() => OuvrirNavigateur("references"));
        RechargerCatalogueReferences = new Commande(RechargerCatalogues);
        OuvrirCatalogueVariateurs = new Commande(() => OuvrirNavigateur("variateurs"));
        OuvrirCatalogueEnveloppes = new Commande(() => OuvrirNavigateur("enveloppes"));
        OuvrirCatalogueSecurite = new Commande(() => OuvrirNavigateur("securite"));
        OuvrirCatalogueCartes = new Commande(() => OuvrirNavigateur("cartes"));
        OuvrirCatalogue = new Commande(() => OuvrirNavigateur("automates"));
        RechargerCatalogue = new Commande(RechargerCatalogues);
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
    public Commande AjouterMoteurFrein { get; }
    public Commande RenommerElement { get; }
    public Commande AjouterCoffret { get; }
    public Commande AjouterEclairage { get; }
    public Commande AjouterPeripherique { get; }
    public Commande SupprimerPeripherique { get; }
    public Commande AjouterAlimentation { get; }
    public Commande AjouterTransformateur { get; }
    public Commande BasculerNote { get; }
    public Commande AfficherBilanPuissance { get; }
    public Commande AjouterPupitre { get; }
    public Commande SupprimerPupitre { get; }
    public Commande ApercuDevis { get; }
    public Commande ApercuRapport { get; }
    public Commande OuvrirCatalogueAlimentations { get; }
    public Commande OuvrirCatalogueIhm { get; }
    public Commande OuvrirCatalogues { get; }

    /// <summary>Panneau de la note de calcul : masqué par défaut pour laisser la place à la saisie.</summary>
    public bool NoteVisible
    {
        get => _noteVisible;
        set { _noteVisible = value; Signaler(); }
    }

    /// <summary>Étapes et tableau du bilan de puissance expliqué.</summary>
    public IReadOnlyList<EtapeBilan> EtapesBilan => ExplicationBilan.Etapes(Resultat);
    public IReadOnlyList<LigneBilan> LignesBilan => ExplicationBilan.Lignes(Resultat);

    /// <summary>Lignes de la demande de devis, pour l'aperçu.</summary>
    public IReadOnlyList<LigneDevis> LignesDevis => ListeDevis.Generer(Resultat);

    /// <summary>Rapport HTML du projet, pour l'aperçu avant PDF.</summary>
    public string RapportHtmlApercu => RapportHtml.Generer(Resultat);

    /// <summary>IHM de la marque de l'automate, pour le choix manuel d'une référence.</summary>
    public IReadOnlyList<ChoixCpu> IhmDisponibles => _ihm
        .Where(m => m.Fabricant.Equals(_projet.Automate.Marque.ToString(), StringComparison.OrdinalIgnoreCase))
        .Select(m => new ChoixCpu(m.Reference, $"{m.Gamme} – {m.Libelle}"))
        .Prepend(new ChoixCpu("", "Automatique, selon la diagonale et la gamme"))
        .ToList();
    public Commande Supprimer { get; }
    public Commande Rapport { get; }
    public Commande ExporterExcel { get; }
    public Commande ExporterSistema { get; }
    public Commande AfficherProjet { get; }
    public Commande AfficherSecurite { get; }
    public Commande AfficherAutomate { get; }
    public Commande AfficherBilan24V { get; }
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
        var a = ArmoireAffichee;
        if (a is null) return (100, 300, 400);
        var longueur = Math.Max(100, Math.Min(400, a.PlaqueLargeur - 2 * a.Laterale));
        return (Math.Round(a.PlaqueX + a.Laterale), Math.Round(a.Hauteur / 2), longueur);
    }

    void AppliquerAction(ActionArmoire action)
    {
        var config = ConfigCourante;
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
        CalculProjet.Calculer(_projet, _catalogue, _references, _variateurs, _enveloppes, _securite, _cartes, _alimentations, _ihm, _capteurs);
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
    public string ResumeArmoire => ArmoireAffichee is { } a
        ? $"{a.Type} {a.Marque} {a.Gamme} {a.Largeur:0} × {a.Hauteur:0} × {a.Profondeur:0} mm{(a.Cellules > 1 ? $", {a.Cellules} cellules" : "")}" +
          $"{(a.TailleImposee ? " (imposée)" : "")} – " +
          $"plaque occupée à {a.Occupation:0} % – {a.Thermique?.PuissanceDissipee:0} W dissipés"
        : "";

    List<ChoixEnveloppe>? _choixEnveloppes;
    IReadOnlyList<ModeleEnveloppe>? _choixSource;

    /// <summary>« Automatique », puis toutes les enveloppes des catalogues Rittal, Legrand et Schneider, de la plus petite à la plus grande.</summary>
    public IReadOnlyList<ChoixEnveloppe> ChoixEnveloppes
    {
        get
        {
            if (_choixEnveloppes is null || !ReferenceEquals(_choixSource, _enveloppes))
            {
                _choixSource = _enveloppes;
                _choixEnveloppes = new List<ChoixEnveloppe> { ChoixEnveloppe.Automatique, ChoixEnveloppe.Libre };
                _choixEnveloppes.AddRange(_enveloppes
                    .Where(m => Enum.TryParse<MarqueEnveloppe>(m.Fabricant, true, out _))
                    .OrderBy(m => m.Fabricant).ThenBy(m => m.Gamme).ThenBy(m => m.Largeur * m.Hauteur).ThenBy(m => m.Profondeur)
                    .Select(m => new ChoixEnveloppe($"{m.Fabricant} {m.Gamme} – {m.Largeur:0} × {m.Hauteur:0} × {m.Profondeur:0} mm ({m.Reference})", m)));
            }
            return _choixEnveloppes;
        }
    }

    /// <summary>Enveloppe imposée : une du catalogue (marque et cotes reprises), des cotes libres, ou le choix automatique.</summary>
    /// <summary>Enveloppe de l'armoire principale (panneau Armoire et implantation).</summary>
    public ChoixEnveloppe EnveloppeChoisie
    {
        get => LireEnveloppe(_projet.Armoire);
        set => EcrireEnveloppe(_projet.Armoire, value);
    }

    /// <summary>Enveloppe de l'armoire ou du coffret affiché dans la vue d'armoire.</summary>
    public ChoixEnveloppe EnveloppeAffichee
    {
        get => LireEnveloppe(ConfigCourante);
        set => EcrireEnveloppe(ConfigCourante, value);
    }

    ChoixEnveloppe LireEnveloppe(ConfigurationArmoire a)
    {
        if (!a.TailleImposee) return ChoixEnveloppe.Automatique;
        return ChoixEnveloppes.FirstOrDefault(c => c.Modele is { } m && a.CellulesImposees == 1
                   && m.Fabricant.Equals(a.Marque.ToString(), StringComparison.OrdinalIgnoreCase)
                   && Math.Abs(m.Largeur - a.LargeurImposee) < 1 && Math.Abs(m.Hauteur - a.HauteurImposee) < 1
                   && Math.Abs(m.Profondeur - a.ProfondeurImposee) < 1)
               ?? ChoixEnveloppe.Libre;
    }

    void EcrireEnveloppe(ConfigurationArmoire a, ChoixEnveloppe? value)
    {
        if (value is null || value == LireEnveloppe(a)) return;
        a.TailleImposee = value != ChoixEnveloppe.Automatique;
        if (value.Modele is { } m)
        {
            a.Marque = Enum.Parse<MarqueEnveloppe>(m.Fabricant, true);
            a.LargeurImposee = m.Largeur;
            a.HauteurImposee = m.Hauteur;
            a.ProfondeurImposee = m.Profondeur;
            a.CellulesImposees = 1;
        }
        Recalculer();
    }

    /// <summary>Place libre à garder dans l'armoire, en %, réglable depuis la vue d'armoire.</summary>
    public double PlaceLibre
    {
        get => ConfigCourante.ReservePourcent;
        set
        {
            var v = Math.Clamp(value, 0, 80);
            if (Math.Abs(v - ConfigCourante.ReservePourcent) < 1e-9) return;
            ConfigCourante.ReservePourcent = v;
            Recalculer();
        }
    }

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
        Depart d => $"NOTE DE CALCUL – {d.Code} {d.Designation}".ToUpperInvariant(),
        ConfigurationSecurite => "NOTE DE CALCUL – SÉCURITÉ MACHINE",
        ConfigurationAutomate => "NOTE DE CALCUL – AUTOMATE",
        PageArmoire => "NOTE DE CALCUL – ARMOIRE ET IMPLANTATION",
        PageThermique => "NOTE DE CALCUL – BILAN THERMIQUE",
        PageSelectivite => "NOTE DE CALCUL – SÉLECTIVITÉ",
        PageReferences => "RÉFÉRENCES DU MATÉRIEL",
        PageBilanPuissance => "NOTE DE CALCUL – BILAN DE PUISSANCE",
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
        if (_projet.AjoutAutomatiqueSources && Resultat.SourcesManquantes.Count > 0)
        {
            _choixAlimentation = null;
            var ajoutees = SourcesCommande.Completer(_projet, Resultat.Consommateurs24V, ChoisirSource, _sourcesIgnorees);
            // Primaire triphasé ou sortie réglable choisis dans la question : appliqués à l'alimentation 24 V DC ajoutée.
            if (_choixAlimentation is { } c)
                foreach (var a in ajoutees.Where(a => a.Sortie == TensionCommande.Continu24 && a.Source == SourceAlimentation.Decoupage))
                {
                    a.Reglable = c.Reglable;
                    if (c.Primaire == TypeAlimentation.Tri400) a.Alimentation = TypeAlimentation.Tri400;
                    a.Designation = DepartAlimentation24V.Libelle(a.Sortie, false, a.Alimentation) + (a.Reglable ? " réglable" : "")
                        + (a.Alimentation == TypeAlimentation.Tri400 ? ", primaire triphasé" : "");
                }
            // Un besoin sans ajout (refusé) ou rattaché à un transformateur existant demande aussi un recalcul.
            Resultat = CalculerProjet();
            if (ajoutees.Count > 0)
                MessageBox.Show("Ajouté automatiquement : " + string.Join(", ", ajoutees.Select(a => $"{a.Code} {a.Designation}")) +
                                ".\nIl est dimensionné sur les consommateurs du projet ; vous pouvez changer sa marque, sa technologie ou le supprimer.",
                    "Sources de commande", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        SignalerTout();
    }

    (TypeAlimentation Primaire, bool Reglable)? _choixAlimentation;

    /// <summary>Technologie d'une source 24 V DC à ajouter : préférence du projet, ou question à l'utilisateur.</summary>
    SourceAlimentation? ChoisirSource(BesoinSource besoin)
    {
        switch (_projet.PreferenceSource)
        {
            case PreferenceSource.Decoupage: return SourceAlimentation.Decoupage;
            case PreferenceSource.Transformateur: return SourceAlimentation.Transformateur;
        }
        var question = new FenetreChoixSource(besoin.Raison) { Owner = Application.Current.MainWindow };
        question.ShowDialog();
        if (question.Choix is not { } choix)
        {
            _sourcesIgnorees.Add(besoin.Tension);
            return null;
        }
        _choixAlimentation = (question.Primaire, question.Reglable);
        if (question.Retenu)
            _projet.PreferenceSource = choix == SourceAlimentation.Decoupage ? PreferenceSource.Decoupage : PreferenceSource.Transformateur;
        return choix;
    }

    static DepartAlimentation24V NouveauTransformateur(TensionCommande sortie)
    {
        var t = new DepartAlimentation24V
        {
            Source = SourceAlimentation.Transformateur, Sortie = sortie, Alimentation = TypeAlimentation.Bi400,
            Differentiel = SensibiliteDifferentiel.Aucun,
        };
        t.Designation = DepartAlimentation24V.Libelle(sortie, true, t.Alimentation);
        return t;
    }

    /// <summary>Repère et désignation choisis par l'utilisateur pour un appareil, gardés dans le projet.</summary>
    void Renommer(ElementMateriel e)
    {
        var fenetre = new FenetreRenommage(e) { Owner = Application.Current.MainWindow };
        if (fenetre.ShowDialog() != true) return;
        var nom = fenetre.Resultat;
        Modifier(() =>
        {
            if (nom is null) _projet.Renommages.Remove(e.CleRenommage);
            else _projet.Renommages[e.CleRenommage] = new Renommage
                { Repere = nom.Repere == e.RepereOrigine ? "" : nom.Repere, Designation = nom.Designation };
        });
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
        if (MessageBox.Show($"Supprimer le départ {d.Code} « {d.Designation} » ?", "Supprimer un départ",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _projet.Departs.Remove(d);
        // Une source supprimée n'est pas rajoutée aussitôt : l'alerte de la note de l'arrivée reste.
        if (d is DepartAlimentation24V source && !_projet.Departs.OfType<DepartAlimentation24V>().Any(a => a.Sortie == source.Sortie))
            _sourcesIgnorees.Add(source.Sortie);
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
        _sourcesIgnorees.Clear();
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

    public async void GenererRapport()
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

    /// <summary>Ouvre le navigateur de catalogues (tableau filtrable et fiche technique) sur une famille.</summary>
    void OuvrirNavigateur(string idFamille) => Afficher(new FenetreCatalogue(this, idFamille));

    /// <summary>Relit tous les catalogues après une modification, puis recalcule le projet.</summary>
    public void RechargerCatalogues()
    {
        _catalogue = CatalogueAutomates.Charger(CheminCatalogue);
        _references = CatalogueReferences.Charger(CheminReferences);
        _variateurs = CatalogueVariateurs.Charger(CheminVariateurs);
        _enveloppes = CatalogueEnveloppes.Charger(CheminEnveloppes);
        _securite = CatalogueSecurite.Charger(CheminSecurite);
        _capteurs = CatalogueCapteursSecurite.Charger(CheminCapteurs);
        CatalogueEncombrements.Actif = CatalogueEncombrements.Charger(CheminEncombrements);
        CatalogueRepartiteurs.Actif = CatalogueRepartiteurs.Charger(CheminRepartiteurs);
        _cartes = CatalogueCartesEs.Charger(CheminCartes);
        _alimentations = CatalogueAlimentations.Charger(CheminAlimentations);
        _ihm = CatalogueIhm.Charger(CheminIhm);
        _cpus = ListerCpus(_catalogue);
        Signaler(nameof(CpusDisponibles));
        Signaler(nameof(IhmDisponibles));
        Recalculer();
    }

    public void EnregistrerDevis()
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
            ModeDemarrage.DeuxVitesses => "Moteur deux vitesses",
            ModeDemarrage.Servomoteur => "Servomoteur",
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
            "Automate" or "Entrées/sorties" or "Accessoire automate" or "IHM" => _projet.Automate,
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

/// <summary>Une entrée de la liste des enveloppes : choix automatique, cotes libres, ou une enveloppe du catalogue.</summary>
public sealed class ChoixEnveloppe
{
    public static readonly ChoixEnveloppe Automatique = new("Automatique : la plus petite qui reçoit le matériel", null);
    public static readonly ChoixEnveloppe Libre = new("Dimensions libres (saisies à la main)", null);

    public ChoixEnveloppe(string libelle, ModeleEnveloppe? modele)
    {
        Libelle = libelle;
        Modele = modele;
    }

    public string Libelle { get; }
    public ModeleEnveloppe? Modele { get; }
    public override string ToString() => Libelle;
}
