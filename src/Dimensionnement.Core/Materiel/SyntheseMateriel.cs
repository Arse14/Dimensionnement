using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Materiel;

/// <summary>Un appareil dans la chaîne d'une carte ; « En parallèle » marque un appareil hors de la ligne principale.</summary>
public record MaillonMateriel(ElementMateriel Element, bool EnParallele);

/// <summary>Une carte : l'arrivée, un départ ou un ensemble de commande, avec sa chaîne d'appareils.</summary>
public record CarteMateriel(string Titre, string SousTitre, StatutCalcul Statut, IReadOnlyList<MaillonMateriel> Chaine, Depart? Depart);

public record GroupeMateriel(string Titre, IReadOnlyList<CarteMateriel> Cartes);

/// <summary>
/// Présentation du matériel en cartes groupées par rubrique : l'arrivée, les départs par rubrique de l'armoire,
/// puis la commande (automate, sécurité). Chaque carte donne la chaîne de l'amont vers l'aval.
/// </summary>
public static class SyntheseMateriel
{
    /// <summary>Titre du groupe de commande (automate, sécurité), distinct d'une rubrique « Commande » de départs.</summary>
    public const string TitreCommande = "AUTOMATISME ET SÉCURITÉ";

    static readonly string[] Commande = { "Automate", "Entrées/sorties", "Accessoire automate", "Sécurité" };

    public static IReadOnlyList<GroupeMateriel> Grouper(ResultatProjet r)
    {
        var groupes = new List<GroupeMateriel>();
        var arrivee = r.Arbre.Parcourir().Where(e => e.RepereOrigine is "W0" or "QS0" or "QG").Select(e => new MaillonMateriel(e, false)).ToList();
        groupes.Add(new GroupeMateriel("ARRIVÉE", new[]
        {
            new CarteMateriel(r.Arbre.Designation, r.Arbre.Calibre, r.NoteArrivee.StatutGlobal, arrivee, null),
        }));

        foreach (var rubrique in r.Departs.GroupBy(d => d.Depart.Rubrique is { Length: > 0 } x ? x : "Puissance"))
        {
            var cartes = rubrique.Select(d => new CarteMateriel(
                $"D{d.Depart.Numero:00}  {d.Depart.Designation}".TrimEnd(),
                d.Depart.TypeLibelle,
                d.Note.StatutGlobal,
                Chaine(d.Materiel),
                d.Depart)).ToList();
            groupes.Add(new GroupeMateriel($"{rubrique.Key.ToUpperInvariant()} ({cartes.Count})", cartes));
        }

        var commande = new List<CarteMateriel>();
        foreach (var racine in r.Arbre.Parcourir().Where(e => e.Famille is "Automate" || (e.Famille == "Sécurité" && !e.RepereOrigine.StartsWith("KM"))))
        {
            var maillons = racine.Parcourir().Where(e => e.Famille != "Accessoire automate").Select(e => new MaillonMateriel(e, false)).ToList();
            commande.Add(new CarteMateriel($"{racine.Repere}  {racine.Designation}", racine.Calibre, racine.Statut, maillons, null));
        }
        if (commande.Count > 0) groupes.Add(new GroupeMateriel(TitreCommande, commande));
        return groupes;
    }

    /// <summary>Chaîne d'un départ : la ligne principale jusqu'au récepteur, les appareils en parallèle juste après leur amont.</summary>
    public static IReadOnlyList<MaillonMateriel> Chaine(ElementMateriel tete)
    {
        var chaine = new List<MaillonMateriel> { new(tete, false) };
        var e = tete;
        while (Principal(e) is { } suivant)
        {
            // L'appareil en parallèle (contacteur sens arrière, étoile…) se lit à côté de son homologue de la ligne principale.
            chaine.Add(new MaillonMateriel(suivant, false));
            foreach (var autre in e.Enfants.Where(c => c != suivant && !Commande.Contains(c.Famille)))
                chaine.Add(new MaillonMateriel(autre, true));
            e = suivant;
        }
        return chaine;
    }

    static ElementMateriel? Principal(ElementMateriel e) =>
        e.Enfants.FirstOrDefault(c => c.Parcourir().Any(x => x.Famille is "Câble" or "Moteur" or "Récepteur" or "Prise de courant"))
        ?? e.Enfants.FirstOrDefault(c => !Commande.Contains(c.Famille));
}
