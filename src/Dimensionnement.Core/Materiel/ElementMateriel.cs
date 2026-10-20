using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Materiel;

/// <summary>Un appareil de l'armoire, placé dans l'arbre d'alimentation et repris dans la nomenclature.</summary>
public class ElementMateriel
{
    public string Repere { get; init; } = "";
    public string Famille { get; init; } = "";
    public string Designation { get; init; } = "";

    /// <summary>Calibre ou caractéristique principale, lisible (« 13–18 A », « 2,5 mm² »…).</summary>
    public string Calibre { get; init; } = "";
    public int Quantite { get; init; } = 1;
    public string Fabricant { get; init; } = "";
    public string Reference { get; init; } = "";
    public StatutCalcul Statut { get; set; } = StatutCalcul.Info;

    /// <summary>Départ auquel l'appareil appartient ; null pour l'arrivée.</summary>
    public Guid? DepartId { get; set; }

    /// <summary>Zone CEM de l'armoire où l'appareil doit être implanté.</summary>
    public ZoneCem Zone => Famille switch
    {
        "Variateur" or "Contacteur" or "Relais thermique" => ZoneCem.Sale,
        "Câble" or "Moteur" or "Récepteur" or "Prise de courant" or "Réseau" => ZoneCem.HorsArmoire,
        "Automate" or "Entrées/sorties" or "Sécurité" => ZoneCem.Propre,
        _ => ZoneCem.Intermediaire,
    };

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
