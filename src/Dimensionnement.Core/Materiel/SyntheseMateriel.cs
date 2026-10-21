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
/// puis la commande (automate, IHM, sécurité). Chaque carte donne la chaîne de l'amont vers l'aval.
/// </summary>
public static class SyntheseMateriel
{
    /// <summary>Titre du groupe de commande (automate, sécurité), distinct d'une rubrique « Commande » de départs.</summary>
    public const string TitreCommande = "AUTOMATISME ET SÉCURITÉ";

    static readonly string[] Commande = { "Automate", "Entrées/sorties", "Accessoire automate", "Sécurité" };

    /// <summary>
    /// Couleurs de l'arborescence : rouge pour un départ non conforme, orange pour un appareil dont la référence est encore
    /// indicative au catalogue (à vérifier), vert sinon. Les alertes restent dans les notes de calcul.
    /// </summary>
    public static void MarquerAVerifier(ResultatProjet r, ISet<string> referencesIndicatives)
    {
        foreach (var e in r.Arbre.Parcourir())
        {
            // Un appareil déjà marqué par le calcul (référence imposée hors catalogue, CPU F sans version F) le reste.
            e.AVerifier = e.AVerifier || e.Reference.Length > 0 && referencesIndicatives.Contains(e.Reference) || e.CotesIncompletes;
            if (e.Statut == StatutCalcul.NonConforme) continue;
            e.Statut = e.AVerifier ? StatutCalcul.Alerte : StatutCalcul.Conforme;
        }
    }

    static StatutCalcul Pire(IEnumerable<MaillonMateriel> chaine) =>
        chaine.Select(m => m.Element.Statut).DefaultIfEmpty(StatutCalcul.Conforme).Max();

    public static IReadOnlyList<GroupeMateriel> Grouper(ResultatProjet r)
    {
        var groupes = new List<GroupeMateriel>();
        var arrivee = r.Arbre.Parcourir().Where(e => e.RepereOrigine is "W0" or "QS0" or "QG").Select(e => new MaillonMateriel(e, false)).ToList();
        groupes.Add(new GroupeMateriel("ARRIVÉE", new[]
        {
            new CarteMateriel(r.Arbre.Designation, r.Arbre.Calibre, Pire(arrivee), arrivee, null),
        }));

        foreach (var rubrique in r.Departs.GroupBy(d => d.Depart.Rubrique is { Length: > 0 } x ? x : "Puissance"))
        {
            var cartes = rubrique.Select(d =>
            {
                var chaine = Chaine(d.Materiel);
                return new CarteMateriel($"{d.Depart.Code}  {d.Depart.Designation}".TrimEnd(), d.Depart.TypeLibelle, Pire(chaine), chaine, d.Depart);
            }).ToList();
            groupes.Add(new GroupeMateriel($"{rubrique.Key.ToUpperInvariant()} ({cartes.Count})", cartes));
        }

        var commande = new List<CarteMateriel>();
        foreach (var racine in r.Arbre.Parcourir().Where(e => e.Famille is "Automate" or "IHM" || (e.Famille == "Sécurité" && !e.RepereOrigine.StartsWith("KM"))))
        {
            var maillons = racine.Parcourir().Where(e => e.Famille != "Accessoire automate").Select(e => new MaillonMateriel(e, false)).ToList();
            // L'alimentation 24 V DC qui alimente l'automate ou la sécurité ouvre la chaîne.
            if (r.Arbre.Parcourir().FirstOrDefault(p => p.Enfants.Contains(racine)) is { Famille: "Alimentation" or CalculProjet.SourceManquante } source)
                maillons.Insert(0, new MaillonMateriel(source, false));
            commande.Add(new CarteMateriel($"{racine.Repere}  {racine.Designation}", racine.Calibre, Pire(maillons), maillons, null));
        }
        if (commande.Count > 0) groupes.Add(new GroupeMateriel(TitreCommande, commande));

        // Ventilation ajoutée par le bilan thermique : protection, thermostat et ventilateurs encastrés.
        var ventilation = r.Arbre.Parcourir().Where(e => e.Famille == "Disjoncteur" && e.RepereOrigine.StartsWith("Q-EV")).Select(q =>
        {
            var maillons = q.Parcourir().Select(e => new MaillonMateriel(e, false)).ToList();
            var ev = q.Parcourir().FirstOrDefault(e => e.Famille == "Ventilation");
            return new CarteMateriel($"{q.Repere}  {q.Designation}", ev?.Calibre ?? "", Pire(maillons), maillons, null);
        }).ToList();
        // Avant l'automatisme, qui reste la dernière rubrique.
        if (ventilation.Count > 0) groupes.Insert(commande.Count > 0 ? groupes.Count - 1 : groupes.Count, new GroupeMateriel($"VENTILATION ({ventilation.Count})", ventilation));
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
