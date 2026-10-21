namespace Dimensionnement.Core.Modele;

/// <summary>Caractéristiques du réseau qui alimente l'armoire et de son câble d'arrivée.</summary>
public class Reseau
{
    public TypeAlimentation Alimentation { get; set; } = TypeAlimentation.Tetra400;
    public SchemaLiaisonTerre Slt { get; set; } = SchemaLiaisonTerre.TT;
    public OrigineAlimentation Origine { get; set; } = OrigineAlimentation.ReseauPublicBT;
    public double FrequenceHz { get; set; } = 50;

    /// <summary>Courant de court-circuit présumé à l'arrivée de l'armoire, en kA.</summary>
    public double IccKiloAmperes { get; set; } = 10;

    /// <summary>Température ambiante autour des câbles, en °C.</summary>
    public double TemperatureAmbiante { get; set; } = 30;

    /// <summary>Câble entre la source et l'armoire.</summary>
    public CaracteristiquesCable CableArrivee { get; set; } = new() { Longueur = 20 };

    /// <summary>
    /// Coefficient d'extension du câble d'arrivée (1 = aucun) : sa section est choisie pour le courant d'emploi × ce coefficient,
    /// pour pouvoir ajouter des départs plus tard sans tirer un nouveau câble.
    /// </summary>
    public double CoefficientExtension { get; set; } = 1;

    /// <summary>
    /// Coefficient de simultanéité ks de l'armoire : 1 par défaut (armoire de machine, tous les départs en service).
    /// Il peut être réduit sur justification, jamais sous la valeur du guide UTE C 15-105 pour le nombre de départs.
    /// </summary>
    public double CoefficientSimultaneite { get; set; } = 1;

    /// <summary>Répartiteur de puissance en aval du disjoncteur général, dimensionné sur les départs qu'il alimente.</summary>
    public bool Repartiteur { get; set; } = true;

    /// <summary>Parafoudre de type 2 en tête d'armoire, avec sa protection.</summary>
    public bool Parafoudre { get; set; }

    /// <summary>Relais de contrôle de phases (ordre, absence, déséquilibre) en tête d'armoire.</summary>
    public bool ControlePhases { get; set; }

    public double TensionComposee => 400;
    public double TensionSimple => 230;
}

/// <summary>Conditions de pose d'un câble.</summary>
public class CaracteristiquesCable
{
    /// <summary>Longueur en mètres.</summary>
    public double Longueur { get; set; } = 10;
    public MethodePose Pose { get; set; } = MethodePose.C;
    public Isolant Isolant { get; set; } = Isolant.PVC;

    /// <summary>Nombre de circuits jointifs posés ensemble (facteur de groupement).</summary>
    public int CircuitsGroupes { get; set; } = 1;
}
