using System.Text.Json.Serialization;

namespace Dimensionnement.Core.Modele;

/// <summary>Départ de l'armoire : une protection, éventuellement un contacteur, un câble et un récepteur.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DepartMoteur), "moteur")]
[JsonDerivedType(typeof(DepartResistif), "resistif")]
[JsonDerivedType(typeof(DepartPrise), "prise")]
[JsonDerivedType(typeof(DepartGenerique), "generique")]
[JsonDerivedType(typeof(DepartCoffret), "coffret")]
[JsonDerivedType(typeof(DepartAlimentation24V), "alimentation24v")]
public abstract class Depart : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void Signaler(params string[] proprietes)
    {
        foreach (var p in proprietes) PropertyChanged?.Invoke(this, new(p));
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Numéro du départ, qui sert à construire les repères (Q1, KM1, M1…).</summary>
    public int Numero { get; set; }
    public string Designation { get; set; } = "";

    /// <summary>Repère choisi par l'utilisateur (ex. =M1, CONVOYEUR 2) ; vide = D + numéro.</summary>
    public string Repere
    {
        get => _repere;
        set
        {
            _repere = value?.Trim() ?? "";
            // L'en-tête de la page du départ suit le nouveau repère.
            PropertyChanged?.Invoke(this, new(nameof(Code)));
        }
    }
    string _repere = "";

    /// <summary>Repère affiché du départ : celui de l'utilisateur, sinon D01, D02…</summary>
    [JsonIgnore] public string Code => _repere.Length > 0 ? _repere : $"D{Numero:00}";

    /// <summary>Rubrique de l'armoire où le départ est rangé (Puissance, Commande, Auxiliaires…).</summary>
    public string Rubrique { get; set; } = "Puissance";

    public TypeAlimentation Alimentation
    {
        get => _alimentation;
        set { _alimentation = value; Modifie(); }
    }
    TypeAlimentation _alimentation = TypeAlimentation.Tri400;

    /// <summary>Appelé quand une donnée d'entrée change, pour les valeurs dérivées affichées.</summary>
    protected virtual void Modifie() { }
    public CaracteristiquesCable Cable { get; set; } = new();
    public SensibiliteDifferentiel Differentiel { get; set; } = SensibiliteDifferentiel.Aucun;
    public TypeProtection Protection { get; set; } = TypeProtection.Disjoncteur;

    /// <summary>Coefficient d'utilisation ku.</summary>
    public double CoefficientUtilisation { get; set; } = 1;

    /// <summary>Valeurs forcées par l'utilisateur, indexées par code de calcul (voir <see cref="Calculs.CodesCalcul"/>).</summary>
    public Dictionary<string, Forcage> Forcages { get; set; } = new();

    [JsonIgnore] public abstract string TypeLibelle { get; }

    /// <summary>Préfixe du repère du récepteur (M pour moteur, R pour résistance…).</summary>
    [JsonIgnore] public abstract string PrefixeRecepteur { get; }
}

public class DepartMoteur : Depart
{
    /// <summary>Puissance utile en kW (plaque).</summary>
    public double PuissanceKw { get => _puissance; set { _puissance = value; Modifie(); } }

    /// <summary>Courant nominal de plaque en A ; 0 = non renseigné.</summary>
    public double CourantPlaque { get => _courant; set { _courant = value; Modifie(); } }
    public double CosPhi { get => _cosPhi; set { _cosPhi = value; Modifie(); } }

    /// <summary>Rendement (rapport entre 0 et 1) ; saisi, ou calculé avec le bouton « Calculer le rendement ».</summary>
    public double Rendement
    {
        get => _rendement;
        set
        {
            _rendement = value;
            OrigineRendement = "";
            Signaler(nameof(Rendement), nameof(OrigineRendement));
        }
    }

    /// <summary>D'où vient le rendement : vide si saisi, sinon la méthode de calcul (« plaque », « couple et vitesse »).</summary>
    public string OrigineRendement { get; set; } = "";

    /// <summary>Couple (N·m) et vitesse (tr/min) nominaux, pour retrouver la puissance utile quand le rendement n'est pas fourni.</summary>
    public double CoupleNominal { get => _couple; set { _couple = value; Modifie(); } }
    public double VitesseNominale { get => _vitesse; set { _vitesse = value; Modifie(); } }

    public Calculs.MethodeRendement MethodeRendement { get => _methode; set { _methode = value; Modifie(); } }

    /// <summary>Rendement retrouvé avec la méthode choisie, ou null s'il manque une donnée de plaque.</summary>
    [JsonIgnore] public Calculs.ResultatRendement? RendementCalcule => Calculs.CalculRendement.Calculer(this, MethodeRendement);

    /// <summary>Applique le rendement calculé et note sa méthode.</summary>
    public bool AppliquerRendementCalcule()
    {
        if (RendementCalcule is not { } r) return false;
        Rendement = Math.Round(r.Rendement, 3);
        OrigineRendement = MethodeRendement == Calculs.MethodeRendement.Plaque ? "calculé (plaque)" : "calculé (couple et vitesse)";
        Signaler(nameof(OrigineRendement));
        return true;
    }

    protected override void Modifie() => Signaler(nameof(RendementCalcule));

    double _puissance, _courant, _cosPhi = 0.85, _rendement = 0.9, _couple, _vitesse;
    Calculs.MethodeRendement _methode;

    /// <summary>Rapport courant de démarrage / courant nominal.</summary>
    public double RapportId { get; set; } = 7;
    public ModeDemarrage Demarrage { get; set; } = ModeDemarrage.Direct;

    /// <summary>Marque du variateur (démarrage par variateur seulement).</summary>
    public MarqueVariateur MarqueVariateur { get; set; } = MarqueVariateur.Projet;

    /// <summary>Moteur deux vitesses : couplage et puissance en petite vitesse (0 = moitié de la grande vitesse).</summary>
    public TypeDeuxVitesses TypeDeuxVitesses { get; set; } = TypeDeuxVitesses.Dahlander;
    public double PuissancePetiteVitesseKw { get; set; }

    /// <summary>Moteur-frein : alimentation et puissance du frein.</summary>
    public bool Frein { get; set; }
    public AlimentationFrein AlimentationFrein { get; set; } = AlimentationFrein.Separee230;
    public double PuissanceFreinW { get; set; } = 25;

    /// <summary>Moteur commandé par un variateur ou un servovariateur (électronique de puissance, câble blindé).</summary>
    [JsonIgnore] public bool SurVariateur => Demarrage is ModeDemarrage.Variateur or ModeDemarrage.Servomoteur;

    public override string TypeLibelle => Demarrage switch
    {
        ModeDemarrage.Direct => "Moteur, démarrage direct",
        ModeDemarrage.Inverseur => "Moteur, inverseur de sens",
        ModeDemarrage.EtoileTriangle => "Moteur, étoile-triangle",
        ModeDemarrage.DemarreurProgressif => "Moteur, démarreur progressif",
        ModeDemarrage.Servomoteur => "Servomoteur",
        ModeDemarrage.DeuxVitesses => "Moteur deux vitesses",
        _ => "Moteur sur variateur",
    };
    public override string PrefixeRecepteur => "M";
}

public class DepartResistif : Depart
{
    public double PuissanceKw { get; set; }
    public bool AvecContacteur { get; set; } = true;

    public override string TypeLibelle => "Charge résistive";
    public override string PrefixeRecepteur => "R";
}

public class DepartPrise : Depart
{
    /// <summary>Calibre du circuit prises en A (16 A, 20 A, 32 A…).</summary>
    public double CalibrePrise { get; set; } = 16;
    public int NombrePrises { get; set; } = 1;

    public DepartPrise()
    {
        Alimentation = TypeAlimentation.Mono230;
        Differentiel = SensibiliteDifferentiel.Ma30;
        CoefficientUtilisation = 0.5;
    }

    public override string TypeLibelle => "Prise de courant";
    public override string PrefixeRecepteur => "XS";
}

/// <summary>Charge quelconque définie par sa puissance absorbée et son cos φ (alimentation 24 V, éclairage…).</summary>
public class DepartGenerique : Depart
{
    public double PuissanceKw { get; set; }
    public double CosPhi { get; set; } = 0.9;
    public CourbeDisjoncteur Courbe { get; set; } = CourbeDisjoncteur.C;

    public override string TypeLibelle => "Charge générique";
    public override string PrefixeRecepteur => "E";
}

/// <summary>
/// Départ vers un coffret déporté (coffret de machine, sous-armoire) : la puissance installée du coffret,
/// réduite par sa propre simultanéité, dimensionne la protection et le câble de liaison.
/// </summary>
public class DepartCoffret : DepartGenerique
{
    /// <summary>Coefficient de simultanéité interne au coffret.</summary>
    public double Simultaneite { get; set; } = 0.8;

    public override string TypeLibelle => "Coffret déporté";
    public override string PrefixeRecepteur => "AC";
}

/// <summary>
/// Alimentation 24 V DC des circuits de commande : dimensionnée sur les consommateurs 24 V du projet
/// (automates, E/S, relais de sécurité, consommateurs déclarés) avec une réserve.
/// </summary>
/// <summary>Partie de la commande qu'une alimentation 24 V dessert.</summary>
public enum UsageAlimentation24V
{
    /// <summary>Toute la commande qu'aucune autre alimentation ne dessert.</summary>
    Commande,
    Automate,
    Securite,
    Actionneurs,
}

/// <summary>Marque de l'alimentation 24 V : celle du projet (Siemens, sinon Schneider), ou une marque imposée.</summary>
public enum MarqueAlimentation { Projet, Siemens, Schneider }

/// <summary>Technologie de la source de commande.</summary>
public enum SourceAlimentation
{
    /// <summary>Alimentation à découpage.</summary>
    Decoupage,
    /// <summary>Transformateur de commande (suivi d'un redresseur filtré pour une sortie continue).</summary>
    Transformateur,
}

/// <summary>Tension d'un circuit de commande : sortie d'une source, bobines de contacteurs, consommateur.</summary>
public enum TensionCommande
{
    Continu24,
    Alternatif24,
    Alternatif230,
}

/// <summary>Préférence du projet pour les sources ajoutées automatiquement.</summary>
public enum PreferenceSource
{
    /// <summary>Le logiciel demande à chaque ajout.</summary>
    Demander,
    Decoupage,
    Transformateur,
}

public class DepartAlimentation24V : Depart
{
    public MarqueAlimentation Marque { get; set; } = MarqueAlimentation.Projet;

    /// <summary>Alimentation saisie à la main quand le catalogue n'a rien qui convienne ; ses caractéristiques sont vérifiées.</summary>
    public bool Manuelle { get; set; }
    public string FabricantManuel { get; set; } = "";
    public string ReferenceManuelle { get; set; } = "";
    public double CourantManuel { get; set; } = 10;
    public double LargeurManuelle { get; set; } = 60;
    public double HauteurManuelle { get; set; } = 125;
    public double ProfondeurManuelle { get; set; } = 125;

    /// <summary>Alimentation à découpage ou transformateur.</summary>
    public SourceAlimentation Source { get; set; } = SourceAlimentation.Decoupage;

    /// <summary>Alimentation à sortie réglable (24-28 V) pour compenser la chute de tension des longues lignes 24 V.</summary>
    public bool Reglable { get; set; }

    /// <summary>Tension de sortie : 24 V DC, 24 V AC ou 230 V AC.</summary>
    public TensionCommande Sortie { get; set; } = TensionCommande.Continu24;

    /// <summary>Départs 230 V alimentés par un transformateur 400/230 V (numéros des départs).</summary>
    public List<int> DepartsAlimentes { get; set; } = new();

    /// <summary>Départs alimentés saisis sous la forme « 3, 5, 7 ».</summary>
    [JsonIgnore]
    public string DepartsAlimentesTexte
    {
        get => string.Join(", ", DepartsAlimentes);
        set => DepartsAlimentes = (value ?? "").Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();
    }

    /// <summary>Ajoutée par le logiciel parce qu'un élément du projet en avait besoin.</summary>
    public bool AjoutAutomatique { get; set; }

    /// <summary>La source est un transformateur (une sortie alternative impose un transformateur).</summary>
    [JsonIgnore] public bool EstTransformateur => Source == SourceAlimentation.Transformateur || Sortie != TensionCommande.Continu24;

    /// <summary>Partie de la commande desservie ; plusieurs alimentations de même usage se partagent la charge.</summary>
    public UsageAlimentation24V Usage { get; set; } = UsageAlimentation24V.Commande;

    /// <summary>Rendement de l'alimentation saisie à la main (celui du catalogue sinon).</summary>
    public double Rendement { get; set; } = 0.9;

    /// <summary>Réserve appliquée sur la consommation, en % (25 % par défaut).</summary>
    public double ReservePourcent { get; set; } = 25;

    public DepartAlimentation24V()
    {
        Alimentation = TypeAlimentation.Mono230;
        Rubrique = "Commande";
        Designation = "Alimentation 24 V DC";
    }

    public override string TypeLibelle => Libelle(Sortie, EstTransformateur, Alimentation);
    public override string PrefixeRecepteur => EstTransformateur ? "T" : "G";

    /// <summary>Libellé d'une source de commande : « Alimentation 24 V DC », « Transformateur 400/24 V AC »…</summary>
    public static string Libelle(TensionCommande sortie, bool transformateur, TypeAlimentation primaire)
    {
        var u = primaire == TypeAlimentation.Mono230 ? "230" : "400";
        return (sortie, transformateur) switch
        {
            (TensionCommande.Continu24, false) => "Alimentation 24 V DC",
            (TensionCommande.Continu24, true) => $"Transformateur {u}/24 V + redresseur 24 V DC",
            (TensionCommande.Alternatif24, _) => $"Transformateur {u}/24 V AC",
            _ => $"Transformateur {u}/230 V AC",
        };
    }
}

/// <summary>Consommateur raccordé au 24 V DC (capteurs, voyants, pupitre, électrovannes…).</summary>
public class Consommateur24V
{
    public string Designation { get; set; } = "";

    /// <summary>Tension du consommateur : 24 V DC par défaut, 24 V AC ou 230 V AC de commande.</summary>
    public TensionCommande Tension { get; set; } = TensionCommande.Continu24;

    /// <summary>Courant unitaire, en mA.</summary>
    public double CourantMa { get; set; }
    public int Quantite { get; set; } = 1;
}

/// <summary>
/// Périphérique monté sur la machine et alimenté par l'armoire (îlot d'électrovannes, colonne lumineuse, éclairage 24 V) :
/// repris au bilan 24 V et au devis, hors de la plaque de fond.
/// </summary>
public class PeripheriqueMachine
{
    public string Nom { get; set; } = "";
    public TypePeripherique Type { get; set; } = TypePeripherique.IlotElectrovannes;
    public string Marque { get; set; } = "";
    public int Quantite { get; set; } = 1;

    /// <summary>Nombre d'électrovannes de l'îlot ou d'étages de la colonne.</summary>
    public int Elements { get; set; } = 8;

    /// <summary>Consommation d'un élément (bobine, étage, réglette) en mA.</summary>
    public double CourantMa { get; set; } = 40;

    /// <summary>Îlot raccordé au bus de terrain de l'automate (PROFINET, EtherNet/IP…) plutôt qu'aux sorties TOR.</summary>
    public bool SurReseau { get; set; } = true;

    /// <summary>Part des éléments alimentés en même temps.</summary>
    public double Simultaneite { get; set; } = 0.5;

    public static double CourantParDefaut(TypePeripherique t) => t switch
    {
        TypePeripherique.IlotElectrovannes => 40,
        TypePeripherique.ColonneLumineuse => 60,
        _ => 400,
    };
}
