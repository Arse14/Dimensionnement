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

/// <summary>Montage de l'interrupteur-sectionneur général.</summary>
public enum MontageSectionneur
{
    /// <summary>Sur la plaque de montage, poignée en porte par axe prolongateur (commande frontale).</summary>
    PlaqueCommandeFrontale,
    /// <summary>Fixé en façade, sur la porte.</summary>
    Porte,
    /// <summary>Sur le côté de l'armoire, à l'extérieur (commande latérale).</summary>
    CoteArmoire,
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

/// <summary>Rail DIN ajouté à la main : extrémité gauche et axe, en mm dans l'enveloppe.</summary>
public class RailManuel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Longueur { get; set; } = 400;
}

/// <summary>Goulotte ajoutée à la main : coin haut gauche en mm dans l'enveloppe, longueur et modèle (largeur × hauteur).</summary>
public class GoulotteManuelle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public double X { get; set; }
    public double Y { get; set; }
    public double Longueur { get; set; } = 400;
    public bool Verticale { get; set; }
    public double Largeur { get; set; } = 40;
    public double Hauteur { get; set; } = 60;
}

/// <summary>Paramètres de l'armoire : dimensions imposées, thermique et implantation manuelle.</summary>
public class ConfigurationArmoire
{
    /// <summary>Place libre à garder sur les rails pour les évolutions, en %.</summary>
    public double ReservePourcent { get; set; } = 25;

    /// <summary>
    /// Dégagement minimal au-dessus et au-dessous des variateurs quand l'armoire est ventilée, en mm (notice du fabricant) :
    /// une armoire trop petite pour les 100 mm habituels devient valable avec une ventilation forcée.
    /// </summary>
    public double DegagementVariateurVentile { get; set; } = 50;

    /// <summary>Montage du sectionneur général : hors plaque (porte, côté), il ne prend pas de place et sort des zones CEM.</summary>
    public MontageSectionneur MontageSectionneur { get; set; } = MontageSectionneur.PlaqueCommandeFrontale;

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

    /// <summary>
    /// Implantation libre : la disposition automatique a servi de base, l'utilisateur la modifie entièrement (appareils,
    /// rails, goulottes) et les contrôles portent sur sa disposition. « Réinitialiser l'implantation » revient à l'automatique.
    /// </summary>
    public bool ImplantationLibre { get; set; }

    /// <summary>Taille imposée ou non avant le passage en implantation libre (qui fige l'enveloppe), rétablie au retour à l'automatique.</summary>
    public bool TailleImposeeAvantLibre { get; set; }

    /// <summary>Appareils déplacés à la main, par repère.</summary>
    public Dictionary<string, PositionManuelle> Positions { get; set; } = new();

    /// <summary>Rails DIN et goulottes ajoutés par l'utilisateur, en plus de l'implantation automatique.</summary>
    public List<RailManuel> Rails { get; set; } = new();
    public List<GoulotteManuelle> Goulottes { get; set; } = new();

    /// <summary>Coffrets supplémentaires où l'on déporte du matériel qui n'entre pas dans l'armoire principale.</summary>
    public List<CoffretDeporte> Coffrets { get; set; } = new();

    /// <summary>Appareils déportés : clé de l'appareil (repère calculé) → coffret qui le reçoit.</summary>
    public Dictionary<string, Guid> Affectations { get; set; } = new();
}

/// <summary>
/// Coffret supplémentaire : mêmes réglages qu'une armoire (marque, taille imposée ou automatique, place libre, implantation
/// manuelle), dimensionné sur le seul matériel qu'on y déporte.
/// </summary>
public class CoffretDeporte : ConfigurationArmoire
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Nom { get; set; } = "Coffret 2";
}

/// <summary>Choix des références de matériel.</summary>
/// <summary>Marque d'une famille d'appareils : celle du projet, ou une marque imposée.</summary>
public enum MarqueFamille { Projet, Schneider, ABB, Siemens, Legrand }

public class ConfigurationReferences
{
    /// <summary>Fabricant de l'appareillage (moteurs et leurs protections) : Siemens par défaut, fournisseur habituel.</summary>
    public FabricantMateriel Fabricant { get; set; } = FabricantMateriel.Siemens;

    /// <summary>Marque des variateurs laissés au choix du projet : Schneider par défaut.</summary>
    public FabricantMateriel MarqueVariateurs { get; set; } = FabricantMateriel.Schneider;

    /// <summary>Marque des contacteurs et de leurs relais thermiques.</summary>
    public MarqueFamille MarqueContacteurs { get; set; } = MarqueFamille.Projet;

    /// <summary>Marque des disjoncteurs, disjoncteurs moteurs et interrupteurs-sectionneurs.</summary>
    public MarqueFamille MarqueDisjoncteurs { get; set; } = MarqueFamille.Projet;

    /// <summary>Fabricant retenu pour une famille d'appareils.</summary>
    public FabricantMateriel FabricantDe(string famille)
    {
        var choix = famille switch
        {
            "Contacteur" or "Relais thermique" => MarqueContacteurs,
            "Disjoncteur" or "Disjoncteur moteur" or "Interrupteur-sectionneur" => MarqueDisjoncteurs,
            _ => MarqueFamille.Projet,
        };
        return choix == MarqueFamille.Projet ? Fabricant : Enum.Parse<FabricantMateriel>(choix.ToString());
    }

    /// <summary>Références saisies à la main, par repère ; elles remplacent celles du catalogue.</summary>
    public Dictionary<string, string> Forcees { get; set; } = new();
}
