using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Materiel;

/// <summary>Un appareil de l'armoire, placé dans l'arbre d'alimentation et repris dans la nomenclature.</summary>
public class ElementMateriel
{
    public string Repere { get; init; } = "";
    public string Famille { get; init; } = "";
    public string Designation { get; set; } = "";

    /// <summary>Calibre ou caractéristique principale, lisible (« 13–18 A », « 2,5 mm² »…).</summary>
    public string Calibre { get; init; } = "";
    public int Quantite { get; init; } = 1;
    public string Fabricant { get; set; } = "";
    public string Reference { get; set; } = "";

    /// <summary>Courant assigné ou de réglage, en A (0 si sans objet).</summary>
    public double Courant { get; set; }

    /// <summary>Plage d'un disjoncteur moteur ou d'un relais thermique, en A (0 si sans objet).</summary>
    public double PlageMin { get; set; }
    public double PlageMax { get; set; }

    /// <summary>Puissance du récepteur ou de l'appareil, en kW (variateur, démarreur, alimentation).</summary>
    public double PuissanceKw { get; set; }

    /// <summary>Nombre de pôles coupés (protection, sectionnement) ; 0 si sans objet.</summary>
    public int Poles { get; set; }

    /// <summary>Courbe de déclenchement d'un disjoncteur (B, C, D) ; vide si sans objet.</summary>
    public string Courbe { get; set; } = "";

    /// <summary>Taille de cartouche d'un porte-fusibles (10×38, NH00…) ; vide si sans objet.</summary>
    public string TailleFusible { get; set; } = "";

    /// <summary>Encombrement en mm et pertes en W du modèle choisi au catalogue ; 0 = estimation moyenne par famille.</summary>
    public double LargeurMm { get; set; }
    public double HauteurMm { get; set; }
    public double ProfondeurMm { get; set; }
    public double PertesW { get; set; }

    /// <summary>Logé dans un autre appareil (module dans un châssis, connecteur frontal) : pas de place propre sur la plaque.</summary>
    public bool Loge { get; set; }

    /// <summary>Monté hors de l'armoire (station déportée en coffret machine) : repris au devis, absent de la plaque.</summary>
    public bool HorsArmoire { get; set; }
    public StatutCalcul Statut { get; set; } = StatutCalcul.Info;

    /// <summary>Départ auquel l'appareil appartient ; null pour l'arrivée.</summary>
    public Guid? DepartId { get; set; }

    /// <summary>Zone CEM de l'armoire où l'appareil doit être implanté.</summary>
    public ZoneCem Zone => Famille switch
    {
        "Variateur" or "Démarreur progressif" or "Contacteur" or "Relais thermique" => ZoneCem.Sale,
        "Câble" or "Moteur" or "Récepteur" or "Prise de courant" or "Réseau" => ZoneCem.HorsArmoire,
        "Automate" or "Entrées/sorties" or "Accessoire automate" or "Sécurité" => ZoneCem.Propre,
        _ => ZoneCem.Intermediaire,
    };

    /// <summary>Renseigne les caractéristiques numériques utilisées par l'armoire, la thermique et les références.</summary>
    public ElementMateriel Avec(double courant = 0, int poles = 0, double puissanceKw = 0, string courbe = "",
        double plageMin = 0, double plageMax = 0, string tailleFusible = "")
    {
        Courant = courant;
        Poles = poles;
        PuissanceKw = puissanceKw;
        Courbe = courbe;
        PlageMin = plageMin;
        PlageMax = plageMax;
        TailleFusible = tailleFusible;
        return this;
    }

    /// <summary>Appareils alimentés par celui-ci.</summary>
    public List<ElementMateriel> Enfants { get; } = new();

    public ElementMateriel Ajouter(ElementMateriel enfant)
    {
        Enfants.Add(enfant);
        return enfant;
    }

    /// <summary>L'élément et tous ses descendants, dans l'ordre de l'arbre.</summary>
    public IEnumerable<ElementMateriel> Parcourir()
    {
        yield return this;
        foreach (var enfant in Enfants)
            foreach (var e in enfant.Parcourir())
                yield return e;
    }
}

/// <summary>Zones d'implantation de l'armoire selon leur niveau de perturbation électromagnétique.</summary>
public enum ZoneCem
{
    /// <summary>Sources de perturbations : variateurs, contacteurs de puissance, filtres.</summary>
    Sale,
    /// <summary>Protections, alimentations, borniers de puissance.</summary>
    Intermediaire,
    /// <summary>Appareils sensibles : automates, E/S, réseaux, relais de sécurité.</summary>
    Propre,
    /// <summary>Hors de l'armoire : câbles et récepteurs.</summary>
    HorsArmoire,
}
