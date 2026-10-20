namespace Dimensionnement.Core.Modele;

/// <summary>Matériau de l'enveloppe, qui fixe son coefficient de transmission thermique.</summary>
public enum MateriauEnveloppe { AcierPeint, Inox, Aluminium, Polyester }

/// <summary>Mode d'installation de l'enveloppe (surfaces qui échangent la chaleur, IEC TR 60890).</summary>
public enum InstallationEnveloppe
{
    Autonome,
    AdosseeAuMur,
    ExtremiteRangee,
    ExtremiteRangeeAdossee,
    MilieuRangee,
    MilieuRangeeAdossee,
}

/// <summary>Fabricant retenu pour le matériel de puissance et de protection.</summary>
public enum FabricantMateriel { Schneider, ABB, Siemens, Legrand }

/// <summary>Fabricant des enveloppes et plaques de montage.</summary>
public enum MarqueEnveloppe { Rittal, Legrand, Schneider }

/// <summary>Marque du variateur d'un départ : celle du projet, ou une marque imposée.</summary>
public enum MarqueVariateur { Projet, ABB, Siemens, Schneider }

/// <summary>Position d'un appareil déplacé à la main sur la plaque de montage, en mm depuis le coin haut gauche.</summary>
public class PositionManuelle
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>Paramètres de l'armoire : dimensions imposées, thermique et implantation manuelle.</summary>
public class ConfigurationArmoire
{
    /// <summary>Place libre à garder sur les rails pour les évolutions, en %.</summary>
    public double ReservePourcent { get; set; } = 25;

    public MarqueEnveloppe Marque { get; set; } = MarqueEnveloppe.Rittal;

    /// <summary>Taille d'armoire bloquée par l'utilisateur : l'implantation doit y tenir, sinon une alerte est levée.</summary>
    public bool TailleImposee { get; set; }
    public double LargeurImposee { get; set; } = 800;
    public double HauteurImposee { get; set; } = 1200;
    public double ProfondeurImposee { get; set; } = 300;
    public int CellulesImposees { get; set; } = 1;

    public MateriauEnveloppe Materiau { get; set; } = MateriauEnveloppe.AcierPeint;
    public InstallationEnveloppe Installation { get; set; } = InstallationEnveloppe.AdosseeAuMur;

    /// <summary>Températures en °C : ambiance maximale et minimale autour de l'armoire, maximum admis à l'intérieur.</summary>
    public double TemperatureAmbianteMax { get; set; } = 35;
    public double TemperatureAmbianteMin { get; set; } = 5;
    public double TemperatureInterieureMax { get; set; } = 40;

    /// <summary>Température intérieure minimale à garantir (condensation, électronique), en °C.</summary>
    public double TemperatureInterieureMin { get; set; } = 10;

    /// <summary>Valeurs forcées de l'armoire et de sa thermique (dimensions, puissance dissipée).</summary>
    public Dictionary<string, Forcage> Forcages { get; set; } = new();

    /// <summary>Appareils déplacés à la main, par repère.</summary>
    public Dictionary<string, PositionManuelle> Positions { get; set; } = new();
}

/// <summary>Choix des références de matériel.</summary>
public class ConfigurationReferences
{
    public FabricantMateriel Fabricant { get; set; } = FabricantMateriel.Schneider;

    /// <summary>Références saisies à la main, par repère ; elles remplacent celles du catalogue.</summary>
    public Dictionary<string, string> Forcees { get; set; } = new();
}
