using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Calculs;

/// <summary>Tension de commande qu'aucune source du projet ne fournit, avec la raison et les départs 230 V concernés.</summary>
public record BesoinSource(TensionCommande Tension, string Raison)
{
    /// <summary>Départs 230 V à alimenter par un transformateur 400/230 V (réseau sans neutre).</summary>
    public IReadOnlyList<int> Departs { get; init; } = Array.Empty<int>();

    /// <summary>Partie de la commande qui demande sa propre alimentation ; « Commande » pour la source commune.</summary>
    public UsageAlimentation24V Usage { get; init; } = UsageAlimentation24V.Commande;
}

/// <summary>
/// Sources de commande du projet : quelle source alimente quoi, ce qui manque, et l'ajout automatique
/// de l'alimentation ou du transformateur nécessaire.
/// </summary>
public static class SourcesCommande
{
    public static string Libelle(TensionCommande t) => t switch
    {
        TensionCommande.Continu24 => "24 V DC",
        TensionCommande.Alternatif24 => "24 V AC",
        _ => "230 V AC",
    };

    /// <summary>Partie de la commande (poste d'un consommateur) → usage d'alimentation qui lui est réservé.</summary>
    public static UsageAlimentation24V UsagePoste(string poste) => poste switch
    {
        ConsommationCommande.Automate => UsageAlimentation24V.Automate,
        ConsommationCommande.Securite => UsageAlimentation24V.Securite,
        ConsommationCommande.Actionneurs => UsageAlimentation24V.Actionneurs,
        _ => UsageAlimentation24V.Commande,
    };

    public static string LibelleUsage(UsageAlimentation24V usage) => usage switch
    {
        UsageAlimentation24V.Automate => "automate et IHM",
        UsageAlimentation24V.Securite => "sécurité",
        UsageAlimentation24V.Actionneurs => "actionneurs",
        _ => "commande",
    };

    /// <summary>Le réseau d'arrivée n'a pas de neutre : les départs 230 V passent par un transformateur 400/230 V.</summary>
    public static bool SansNeutre(Projet projet) => projet.Reseau.Alimentation == TypeAlimentation.Tri400;

    /// <summary>Départ alimenté → transformateur 400/230 V qui l'alimente (le premier qui le déclare).</summary>
    public static Dictionary<Guid, Guid> AlimentesPar(Projet projet)
    {
        var resultat = new Dictionary<Guid, Guid>();
        foreach (var t in projet.Departs.OfType<DepartAlimentation24V>().Where(t => t.Sortie == TensionCommande.Alternatif230))
            foreach (var n in t.DepartsAlimentes)
                if (projet.Departs.FirstOrDefault(d => d.Numero == n) is { } d && d.Id != t.Id
                    && d is not DepartAlimentation24V { Sortie: TensionCommande.Alternatif230 })
                    resultat.TryAdd(d.Id, t.Id);
        return resultat;
    }

    /// <summary>Départs 230 V monophasés d'un réseau sans neutre qu'aucun transformateur n'alimente.</summary>
    public static IReadOnlyList<Depart> DepartsSansSource(Projet projet)
    {
        if (!SansNeutre(projet)) return Array.Empty<Depart>();
        var alimentes = AlimentesPar(projet);
        return projet.Departs.Where(d => d.Alimentation == TypeAlimentation.Mono230 && !alimentes.ContainsKey(d.Id)).ToList();
    }

    /// <summary>
    /// Clé d'un besoin de source refusé : tension et partie alimentée. Refuser l'alimentation réservée à l'automate ne fait
    /// pas taire la question pour la sécurité, ni pour l'alimentation commune.
    /// </summary>
    public static string Cle(TensionCommande tension, UsageAlimentation24V usage) => $"{tension}|{usage}";

    /// <summary>Tensions de commande consommées par le projet et qu'aucune source ne fournit.</summary>
    public static IReadOnlyList<BesoinSource> Manquantes(Projet projet, IReadOnlyList<ConsommationCommande> consommateurs)
    {
        var sources = projet.Departs.OfType<DepartAlimentation24V>().ToList();
        var dedies = projet.UsagesDedies.ToList();
        // Une alimentation réservée à une partie ne couvre pas le reste de la commande.
        bool Commune(DepartAlimentation24V s, TensionCommande t) => s.Sortie == t && (t != TensionCommande.Continu24 || !dedies.Contains(s.Usage));
        var besoins = new List<BesoinSource>();
        foreach (var tension in Enum.GetValues<TensionCommande>())
        {
            var postes = consommateurs.Where(c => c.Tension == tension && c.Courant > 0
                    && !(tension == TensionCommande.Continu24 && dedies.Contains(UsagePoste(c.Poste))))
                .Select(c => c.Poste).Distinct().ToList();
            var departs = tension == TensionCommande.Alternatif230 ? DepartsSansSource(projet) : Array.Empty<Depart>();
            if (postes.Count == 0 && departs.Count == 0) continue;
            if (postes.Count > 0 && sources.Any(s => Commune(s, tension)) && departs.Count == 0) continue;
            var raisons = new List<string>();
            if (postes.Count > 0 && !sources.Any(s => Commune(s, tension)))
                raisons.Add($"{string.Join(", ", postes)} en {Libelle(tension)}");
            if (departs.Count > 0)
                raisons.Add($"départ(s) {string.Join(", ", departs.Select(d => d.Numero))} en 230 V sur un réseau sans neutre");
            if (raisons.Count == 0) continue;
            besoins.Add(new BesoinSource(tension,
                $"Le projet demande du {Libelle(tension)} ({string.Join(" ; ", raisons)}) : " +
                (tension == TensionCommande.Continu24 ? "ajouter une alimentation 24 V DC ou un transformateur avec redresseur."
                    : tension == TensionCommande.Alternatif24 ? "ajouter un transformateur 400/24 V AC ou 230/24 V AC."
                    : "ajouter un transformateur 400/230 V."))
            { Departs = departs.Select(d => d.Numero).ToList() });
        }
        // Parties qui demandent leur propre alimentation 24 V DC, dimensionnée sur leurs seuls consommateurs.
        foreach (var usage in dedies)
        {
            var courant = consommateurs.Where(c => c.Tension == TensionCommande.Continu24 && UsagePoste(c.Poste) == usage).Sum(c => c.Courant);
            if (courant <= 0 || sources.Any(s => s.Sortie == TensionCommande.Continu24 && s.Usage == usage)) continue;
            besoins.Add(new BesoinSource(TensionCommande.Continu24,
                $"Alimentation 24 V DC séparée demandée pour {LibelleUsage(usage)} ({Format.A(courant)}) : " +
                "ajouter une alimentation 24 V DC réservée à cette partie.") { Usage = usage });
        }
        return besoins;
    }

    /// <summary>Crée la source qui couvre un besoin : primaire 230 V si le réseau a un neutre, 400 V entre phases sinon.</summary>
    public static DepartAlimentation24V Creer(Projet projet, BesoinSource besoin, SourceAlimentation source)
    {
        var transformateur = source == SourceAlimentation.Transformateur || besoin.Tension != TensionCommande.Continu24;
        var primaire = besoin.Tension == TensionCommande.Alternatif230 || SansNeutre(projet) ? TypeAlimentation.Bi400 : TypeAlimentation.Mono230;
        var depart = new DepartAlimentation24V
        {
            Source = transformateur ? SourceAlimentation.Transformateur : SourceAlimentation.Decoupage,
            Sortie = besoin.Tension,
            Alimentation = primaire,
            AjoutAutomatique = true,
            DepartsAlimentes = besoin.Departs.ToList(),
            Differentiel = SensibiliteDifferentiel.Aucun,
            Usage = besoin.Usage,
        };
        depart.Designation = DepartAlimentation24V.Libelle(depart.Sortie, transformateur, primaire)
            + (besoin.Usage == UsageAlimentation24V.Commande ? "" : $" – {LibelleUsage(besoin.Usage)}");
        return depart;
    }

    /// <summary>
    /// Ajoute les sources qui manquent. <paramref name="choisir"/> donne la technologie d'une source 24 V DC
    /// (null = ne pas l'ajouter) ; les sorties alternatives sont toujours des transformateurs. Les départs 230 V non
    /// alimentés d'un réseau sans neutre rejoignent le premier transformateur 400/230 V existant.
    /// </summary>
    public static IReadOnlyList<DepartAlimentation24V> Completer(Projet projet, IReadOnlyList<ConsommationCommande> consommateurs,
        Func<BesoinSource, SourceAlimentation?> choisir, ISet<string>? ignorees = null)
    {
        var ajoutees = new List<DepartAlimentation24V>();
        foreach (var besoin in Manquantes(projet, consommateurs))
        {
            if (ignorees?.Contains(Cle(besoin.Tension, besoin.Usage)) == true) continue;
            var existant = besoin.Usage == UsageAlimentation24V.Commande
                ? projet.Departs.OfType<DepartAlimentation24V>().FirstOrDefault(t => t.Sortie == besoin.Tension && !projet.UsagesDedies.Contains(t.Usage))
                : projet.Departs.OfType<DepartAlimentation24V>().FirstOrDefault(t => t.Sortie == besoin.Tension && t.Usage == besoin.Usage);
            if (existant is not null)
            {
                existant.DepartsAlimentes = existant.DepartsAlimentes.Concat(besoin.Departs).Distinct().ToList();
                continue;
            }
            var source = besoin.Tension == TensionCommande.Continu24 ? choisir(besoin) : SourceAlimentation.Transformateur;
            if (source is null) continue;
            ajoutees.Add(projet.Ajouter(Creer(projet, besoin, source.Value)));
        }
        return ajoutees;
    }
}
