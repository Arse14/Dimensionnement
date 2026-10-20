using System.Text.Json.Serialization;

namespace Dimensionnement.Core.Modele;

/// <summary>Départ de l'armoire : une protection, éventuellement un contacteur, un câble et un récepteur.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DepartMoteur), "moteur")]
[JsonDerivedType(typeof(DepartResistif), "resistif")]
[JsonDerivedType(typeof(DepartPrise), "prise")]
[JsonDerivedType(typeof(DepartGenerique), "generique")]
[JsonDerivedType(typeof(DepartAlimentation24V), "alimentation24v")]
public abstract class Depart
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Numéro du départ, qui sert à construire les repères (Q1, KM1, M1…).</summary>
    public int Numero { get; set; }
    public string Designation { get; set; } = "";

    /// <summary>Rubrique de l'armoire où le départ est rangé (Puissance, Commande, Auxiliaires…).</summary>
    public string Rubrique { get; set; } = "Puissance";

    public TypeAlimentation Alimentation { get; set; } = TypeAlimentation.Tri400;
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
    public double PuissanceKw { get; set; }

    /// <summary>Courant nominal de plaque en A ; 0 = non renseigné.</summary>
    public double CourantPlaque { get; set; }
    public double CosPhi { get; set; } = 0.85;
    public double Rendement { get; set; } = 0.9;

    /// <summary>Rapport courant de démarrage / courant nominal.</summary>
    public double RapportId { get; set; } = 7;
    public ModeDemarrage Demarrage { get; set; } = ModeDemarrage.Direct;

    public override string TypeLibelle => Demarrage switch
    {
        ModeDemarrage.Direct => "Moteur, démarrage direct",
        ModeDemarrage.Inverseur => "Moteur, inverseur de sens",
        ModeDemarrage.EtoileTriangle => "Moteur, étoile-triangle",
        ModeDemarrage.DemarreurProgressif => "Moteur, démarreur progressif",
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
/// Alimentation 24 V DC des circuits de commande : dimensionnée sur les consommateurs 24 V du projet
/// (automates, E/S, relais de sécurité, consommateurs déclarés) avec une réserve.
/// </summary>
public class DepartAlimentation24V : Depart
{
    /// <summary>Rendement de l'alimentation à découpage.</summary>
    public double Rendement { get; set; } = 0.9;

    /// <summary>Réserve appliquée sur la consommation, en % (25 % par défaut).</summary>
    public double ReservePourcent { get; set; } = 25;

    public DepartAlimentation24V()
    {
        Alimentation = TypeAlimentation.Mono230;
        Rubrique = "Commande";
        Designation = "Alimentation 24 V DC";
    }

    public override string TypeLibelle => "Alimentation 24 V DC";
    public override string PrefixeRecepteur => "G";
}

/// <summary>Consommateur raccordé au 24 V DC (capteurs, voyants, pupitre, électrovannes…).</summary>
public class Consommateur24V
{
    public string Designation { get; set; } = "";

    /// <summary>Courant unitaire, en mA.</summary>
    public double CourantMa { get; set; }
    public int Quantite { get; set; } = 1;
}
