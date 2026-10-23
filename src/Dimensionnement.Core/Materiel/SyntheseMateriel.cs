using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Materiel;

/// <summary>Un appareil dans la chaîne d'une carte ; « En parallèle » marque un appareil hors de la ligne principale.</summary>
public record MaillonMateriel(ElementMateriel Element, bool EnParallele);

/// <summary>Une carte : l'arrivée, un départ ou un ensemble de commande, avec sa chaîne d'appareils.</summary>
public record CarteMateriel(string Titre, string SousTitre, StatutCalcul Statut, IReadOnlyList<MaillonMateriel> Chaine, Depart? Depart)
{
    /// <summary>Alerte affichée sous la carte, avec sa correction proposée (sélectivité sous le général).</summary>
    public EncartAlerte? Encart { get; init; }
    /// <summary>Étiquette orange à côté du titre (ex. « Sélectivité avec QG »), vide sinon.</summary>
    public string Etiquette { get; init; } = "";
}

/// <summary>Encart d'alerte d'une carte : le problème, son risque et l'alerte qui porte l'action proposée.</summary>
public record EncartAlerte(string Titre, string Detail, Alerte Alerte);

/// <summary>
/// Un groupe de cartes : une rubrique de départs, ou un domaine de tension (tout ce qu'une alimentation de commande
/// alimente) ; <see cref="Source"/> est alors l'alimentation qui le nourrit.
/// </summary>
public record GroupeMateriel(string Titre, IReadOnlyList<CarteMateriel> Cartes)
{
    public ElementMateriel? Source { get; init; }

    /// <summary>Le groupe est un domaine de tension, nourri par une alimentation de commande.</summary>
    public bool Domaine => Source is not null;
}

/// <summary>
/// Présentation du matériel en cartes groupées par rubrique : l'arrivée, les départs par rubrique de l'armoire,
/// puis la commande (automate, IHM, sécurité). Chaque carte donne la chaîne de l'amont vers l'aval.
/// </summary>
public static class SyntheseMateriel
{
    /// <summary>Familles de commande : raccordées à leur alimentation, elles ne prolongent pas la chaîne de puissance d'un départ.</summary>
    static readonly string[] Commande = { "Automate", "Entrées/sorties", "Accessoire automate", "Sécurité", "IHM", "Périphérique machine" };

    /// <summary>Titre du domaine de tension nourri par une alimentation : « 24 V DC – G5 », « 24 V DC – G6 (AUTOMATE ET IHM) ».</summary>
    public static string TitreDomaine(ElementMateriel source, Depart? depart) =>
        source.Famille == CalculProjet.SourceManquante
            ? "24 V DC – ALIMENTATION À AJOUTER"
            : $"{(depart is DepartAlimentation24V a ? SourcesCommande.Libelle(a.Sortie) : "24 V DC")} – {source.Repere}" +
              (depart is DepartAlimentation24V { Usage: not UsageAlimentation24V.Commande } u
                  ? $" ({SourcesCommande.LibelleUsage(u.Usage).ToUpperInvariant()})" : "");

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
        // Sélectivité non assurée sous le général : encart sur l'arrivée, étiquette sur chaque départ concerné.
        var selectivite = r.Alertes.Actives().Where(a => a.Action == ActionAlerte.RemonterGeneral).ToList();
        var nonSelectifs = selectivite.Select(a => a.Point.Replace("Sélectivité avec ", "")).ToHashSet();
        var encart = selectivite.Count == 0 ? null : new EncartAlerte(
            $"Sélectivité non assurée avec {string.Join(", ", nonSelectifs)}",
            $"Disjoncteur général {Format.A(r.CalibreGeneral)} : rapport des calibres sous {Format.N(CalculSelectivite.RapportSurchargeMini)}. " +
            "Une surcharge de ce départ peut faire déclencher le général et couper toute l'armoire.", selectivite[0]);
        groupes.Add(new GroupeMateriel("ARRIVÉE", new[]
        {
            new CarteMateriel(r.Arbre.Designation, r.Arbre.Calibre, Pire(arrivee), arrivee, null) { Encart = encart },
        }));

        foreach (var rubrique in r.Departs.GroupBy(d => d.Depart.Rubrique is { Length: > 0 } x ? x : "Puissance"))
        {
            var cartes = rubrique.Select(d =>
            {
                var chaine = Chaine(d.Materiel);
                return new CarteMateriel($"{d.Depart.Code}  {d.Depart.Designation}".TrimEnd(), d.Depart.TypeLibelle, Pire(chaine), chaine, d.Depart)
                    { Etiquette = nonSelectifs.Contains($"{d.Depart.Code}") ? "Sélectivité avec QG" : "" };
            }).ToList();
            groupes.Add(new GroupeMateriel($"{rubrique.Key.ToUpperInvariant()} ({cartes.Count})", cartes));
        }

        // Domaines de tension : tout ce qu'une alimentation de commande nourrit (automate, IHM, sécurité, périphériques machine)
        // forme un groupe sous elle, un par alimentation ; l'alimentation elle-même reste dans la carte de son départ.
        var domaines = new List<GroupeMateriel>();
        foreach (var source in r.Arbre.Parcourir().Where(e => e.Famille is "Alimentation" or CalculProjet.SourceManquante))
        {
            var cartes = source.Enfants
                .Where(e => e.Famille is "Automate" or "IHM" or "Périphérique machine" || (e.Famille == "Sécurité" && !e.RepereOrigine.StartsWith("KM")))
                .Select(racine =>
                {
                    var maillons = racine.Parcourir().Where(e => e.Famille != "Accessoire automate").Select(e => new MaillonMateriel(e, false)).ToList();
                    return new CarteMateriel($"{racine.Repere}  {racine.Designation}", racine.Calibre, Pire(maillons), maillons, null);
                }).ToList();
            if (cartes.Count == 0) continue;
            // L'alimentation manquante n'a pas de départ : elle ouvre son domaine en rouge.
            if (source.Famille == CalculProjet.SourceManquante)
                cartes.Insert(0, new CarteMateriel($"{source.Repere}  {source.Designation}", source.Calibre, source.Statut,
                    new[] { new MaillonMateriel(source, false) }, null));
            var depart = r.Departs.FirstOrDefault(d => d.Materiel.Parcourir().Contains(source))?.Depart;
            domaines.Add(new GroupeMateriel(TitreDomaine(source, depart), cartes) { Source = source });
        }
        groupes.AddRange(domaines);

        // Ventilation ajoutée par le bilan thermique : protection, thermostat et ventilateurs encastrés.
        var ventilation = r.Arbre.Parcourir().Where(e => e.Famille == "Disjoncteur" && e.RepereOrigine.StartsWith("Q-EV")).Select(q =>
        {
            var maillons = q.Parcourir().Select(e => new MaillonMateriel(e, false)).ToList();
            var ev = q.Parcourir().FirstOrDefault(e => e.Famille == "Ventilation");
            return new CarteMateriel($"{q.Repere}  {q.Designation}", ev?.Calibre ?? "", Pire(maillons), maillons, null);
        }).ToList();
        // Avant les domaines de tension, qui restent les derniers groupes.
        if (ventilation.Count > 0) groupes.Insert(groupes.Count - domaines.Count, new GroupeMateriel($"VENTILATION ({ventilation.Count})", ventilation));
        // Chauffage anticondensation demandé et climatiseur retenu : protection et appareils.
        var chauffage = r.Arbre.Parcourir().Where(e => e.Famille == "Disjoncteur" && (e.RepereOrigine.StartsWith("Q-CH") || e.RepereOrigine.StartsWith("Q-CL"))).Select(q =>
        {
            var maillons = q.Parcourir().Select(e => new MaillonMateriel(e, false)).ToList();
            return new CarteMateriel($"{q.Repere}  {q.Designation}", q.Parcourir().FirstOrDefault(e => e.Famille is "Chauffage" or "Climatisation")?.Calibre ?? "",
                Pire(maillons), maillons, null);
        }).ToList();
        if (chauffage.Count > 0) groupes.Insert(groupes.Count - domaines.Count, new GroupeMateriel($"CHAUFFAGE ET CLIMATISATION ({chauffage.Count})", chauffage));
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
