using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Materiel;

/// <summary>Un appareil de l'armoire, placé dans l'arbre d'alimentation et repris dans la nomenclature.</summary>
public class ElementMateriel
{
    public string Repere { get; set; } = "";

    /// <summary>Repère calculé avant un renommage par l'utilisateur ; sert de clé stable (positions, forçages).</summary>
    public string RepereOrigine { get => _repereOrigine ?? Repere; set => _repereOrigine = value; }
    string? _repereOrigine;

    /// <summary>Clé du renommage dans le projet : départ d'appartenance et repère calculé.</summary>
    public string CleRenommage => $"{DepartId?.ToString() ?? "projet"}|{RepereOrigine}";
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

    /// <summary>Encastré dans la porte de l'armoire (IHM) : pas de place sur la plaque, mais ses pertes chauffent l'armoire.</summary>
    public bool EnPorte { get; set; }

    /// <summary>Encastré dans une paroi de l'enveloppe (ventilateur à filtre) : pas de place sur la plaque.</summary>
    public bool SurParoi { get; set; }

    /// <summary>Monté à l'extérieur de l'enveloppe (sectionneur sur le côté) : ses pertes ne chauffent pas l'armoire.</summary>
    public bool ExterieurEnveloppe { get; set; }

    /// <summary>Coffret déporté imposé par le calcul (ventilation d'un coffret) ; null = selon l'affectation de l'utilisateur.</summary>
    public Guid? CoffretId { get; set; }

    /// <summary>Variateur livré avec son filtre CEM réseau : le devis n'en ajoute pas.</summary>
    public bool FiltreCemIntegre { get; set; }

    /// <summary>Variateur livré avec sa plaque ou ses étriers de reprise de blindage : le devis n'ajoute pas de colliers.</summary>
    public bool BlindageFourni { get; set; }
    public StatutCalcul Statut { get; set; } = StatutCalcul.Info;

    /// <summary>Cotes pas encore identifiées en largeur, hauteur et profondeur : valeurs par défaut, à compléter.</summary>
    public bool CotesIncompletes { get; set; }

    /// <summary>Sa référence vient d'un catalogue dont les valeurs sont encore indicatives (à vérifier sur la fiche du fabricant).</summary>
    public bool AVerifier { get; set; }

    /// <summary>
    /// Faux quand aucune référence générique de sa famille ne convient (servovariateur, variateur monophasé, transformateur,
    /// alimentation de frein, appareil pour courant continu…) : la référence reste à compléter au lieu d'être devinée.
    /// </summary>
    public bool ReferenceAuto { get; set; } = true;

    /// <summary>Départ auquel l'appareil appartient ; null pour l'arrivée.</summary>
    public Guid? DepartId { get; set; }

    /// <summary>Zone CEM de l'armoire où l'appareil doit être implanté.</summary>
    public ZoneCem Zone => SurParoi ? ZoneCem.HorsArmoire : Famille switch
    {
        "Variateur" or "Démarreur progressif" or "Contacteur" or "Relais thermique" => ZoneCem.Sale,
        "Câble" or "Moteur" or "Récepteur" or "Prise de courant" or "Réseau" or "Capteur de sécurité" or "Périphérique machine" => ZoneCem.HorsArmoire,
        "Automate" or "Entrées/sorties" or "Accessoire automate" or "Sécurité" or "IHM" => ZoneCem.Propre,
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
