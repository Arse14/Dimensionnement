using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Armoire;

/// <summary>Un appareil placé sur la plaque de montage ; coordonnées en mm depuis le coin haut gauche.</summary>
public class AppareilImplante
{
    public required string Repere { get; init; }
    public required string Designation { get; init; }
    public required string Famille { get; init; }
    public required ZoneCem Zone { get; init; }
    public ElementMateriel? Element { get; init; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double DissipationW { get; init; }

    /// <summary>Vrai si l'utilisateur a déplacé l'appareil à la main.</summary>
    public bool Manuel { get; set; }

    public double CentreX => X + Largeur / 2;
    public double CentreY => Y + Hauteur / 2;

    public bool Chevauche(AppareilImplante a) =>
        X < a.X + a.Largeur - 0.5 && a.X < X + Largeur - 0.5 && Y < a.Y + a.Hauteur - 0.5 && a.Y < Y + Hauteur - 0.5;
}

/// <summary>Bande d'une zone CEM sur la plaque (une rangée de rail et ses goulottes).</summary>
public record BandeZone(ZoneCem Zone, double X, double Y, double Largeur, double Hauteur);

/// <summary>Source de perturbations : centre, rayon d'influence indicatif en mm et intensité relative de 0 à 1.</summary>
public record SourceChamp(string Repere, double X, double Y, double Rayon, double Intensite);

public class ResultatArmoire
{
    public required NoteDeCalcul Note { get; init; }

    /// <summary>Dimensions d'une cellule, en mm.</summary>
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public int Cellules { get; init; } = 1;
    public double LargeurTotale => Largeur * Cellules;

    /// <summary>« Coffret mural » ou « Armoire au sol ».</summary>
    public string Type { get; init; } = "";
    public IReadOnlyList<AppareilImplante> Appareils { get; init; } = Array.Empty<AppareilImplante>();
    public IReadOnlyList<BandeZone> Bandes { get; init; } = Array.Empty<BandeZone>();
    public IReadOnlyList<SourceChamp> Sources { get; init; } = Array.Empty<SourceChamp>();

    /// <summary>Longueur de rail DIN et de goulotte nécessaires, en m (pour la liste de matériel).</summary>
    public double LongueurRail { get; init; }
    public double LongueurGoulotte { get; init; }

    /// <summary>Hauteur de plaque occupée, en % de la hauteur disponible de toutes les cellules.</summary>
    public double Occupation { get; init; }
}

/// <summary>
/// Estime l'armoire : implantation sur rails par zone CEM (propre en haut, intermédiaire, sale en bas près des
/// entrées de câbles de puissance), choix de la plus petite enveloppe normalisée, contrôle des déplacements manuels
/// et des distances entre sources de perturbations et appareils sensibles.
/// </summary>
public static class CalculArmoire
{
    public const string CodeLargeur = "LA";
    public const string CodeHauteur = "HA";
    public const string CodeProfondeur = "PA";
    public const string CodeCellules = "NC";

    const string RefCem = "Directive 2014/30/UE ; guides CEM des fabricants (séparation des zones, distance ≥ 200 mm)";
    const string RefArmoire = "NF EN 61439-1 ; NF EN 60204-1, 11.2 (implantation et accessibilité)";

    /// <summary>Largeur des goulottes verticales de chaque côté, en mm.</summary>
    public const double MargeLaterale = 50;
    /// <summary>Espace libre en haut de plaque et en bas (presse-étoupes, arrivée des câbles), en mm.</summary>
    public const double MargeHaute = 60;
    public const double MargeBasse = 120;
    /// <summary>Goulotte horizontale et dégagement entre deux rangées, en mm.</summary>
    public const double EntreRangees = 80;
    /// <summary>Distance minimale conseillée entre la zone sale et la zone propre, en mm.</summary>
    public const double SeparationCem = 200;
    /// <summary>Jeu entre deux appareils de départs différents, en mm.</summary>
    const double JeuDeparts = 5;

    static readonly double[] LargeursCoffret = { 300, 400, 500, 600, 800, 1000 };
    static readonly double[] HauteursCoffret = { 300, 400, 500, 600, 700, 800, 1000, 1200, 1400 };
    static readonly double[] LargeursArmoire = { 600, 800, 1000, 1200 };
    static readonly double[] HauteursArmoire = { 1800, 2000 };
    static readonly double[] ProfondeursCoffret = { 200, 250, 300, 400 };
    static readonly double[] ProfondeursArmoire = { 400, 500, 600, 800 };
    const int CellulesMax = 8;

    public static ResultatArmoire Calculer(ResultatProjet r, Projet projet)
    {
        var config = projet.Armoire;
        var note = new NoteDeCalcul("ARM", config.Forcages);
        var elements = AppareilsArmoire(r, projet);
        var reserve = Math.Clamp(config.ReservePourcent, 0, 80) / 100;

        // Plus petite enveloppe normalisée qui reçoit tout le matériel avec la réserve demandée.
        var candidats = Candidats().OrderBy(c => c.L * c.N * c.H).ThenBy(c => c.H);
        var auto = candidats.FirstOrDefault(c => Implanter(elements, c.L, c.H, c.N, reserve).Tient);
        if (auto == default) auto = (1200, 2000, CellulesMax, false);

        var largeurs = elements.Count == 0 ? 0 : elements.Sum(e => e.Largeur);
        var entrees = $"{elements.Count} appareil(s) et bornier(s), {N(largeurs / 1000)} m de rail, réserve {N(config.ReservePourcent)} %";
        var largeur = note.Valeur(CodeLargeur, "Largeur d'une cellule", auto.L, auto.L, "mm",
            "Plus petite enveloppe normalisée qui reçoit le matériel", entrees, RefArmoire);
        var hauteur = note.Valeur(CodeHauteur, "Hauteur", auto.H, auto.H, "mm",
            "Rangées par zone CEM + goulottes + marges", entrees, RefArmoire);
        var cellules = (int)Math.Max(1, Math.Round(note.Valeur(CodeCellules, "Nombre de cellules juxtaposées", auto.N, auto.N, "",
            "Cellules supplémentaires quand la hauteur ne suffit plus", "", RefArmoire)));
        var armoire = hauteur >= 1600;

        var profondeurAppareils = elements.Count == 0 ? 0 : elements.Max(e => e.Profondeur);
        var profondeurMini = profondeurAppareils + 100;
        var profondeurs = armoire ? ProfondeursArmoire : ProfondeursCoffret;
        var profondeurAuto = Calibres.Superieur(profondeurs, profondeurMini) ?? profondeurs[^1];
        var profondeur = note.Valeur(CodeProfondeur, "Profondeur", profondeurMini, profondeurAuto, "mm",
            "P ≥ profondeur du plus gros appareil + 100 mm (plaque, porte, câblage)",
            $"appareil le plus profond : {N(profondeurAppareils)} mm", RefArmoire);
        note.Verifier("Profondeur", profondeur >= profondeurMini,
            $"Profondeur {N(profondeur)} mm pour {N(profondeurMini)} mm nécessaires.", RefArmoire, StatutCalcul.Alerte);

        var implantation = Implanter(elements, largeur, hauteur, cellules, reserve);
        note.Info("Enveloppe", $"{(armoire ? "Armoire au sol" : "Coffret mural")} {N(largeur)} × {N(hauteur)} × {N(profondeur)} mm" +
            (cellules > 1 ? $", {cellules} cellules juxtaposées (largeur totale {N(largeur * cellules)} mm)" : "") + ".", RefArmoire);
        note.Verifier("Implantation", implantation.Tient,
            implantation.Tient
                ? $"Tout le matériel tient avec {N(config.ReservePourcent)} % de réserve ; hauteur occupée {Pct(implantation.Occupation)}."
                : "Le matériel ne tient pas dans les dimensions retenues : agrandir l'armoire ou réduire la réserve.",
            RefArmoire, StatutCalcul.NonConforme);
        note.Info("Zones CEM", "Zone propre en haut (automate, E/S, sécurité), zone intermédiaire (protections, alimentations), " +
            "zone sale en bas (contacteurs, variateurs), au plus près des presse-étoupes des câbles moteurs.", RefCem);

        var appareils = implantation.Appareils;
        AppliquerPositionsManuelles(appareils, config, largeur * cellules, hauteur);
        ControlerManuel(note, appareils, implantation.Bandes, largeur * cellules, hauteur);

        var sources = Sources(appareils);
        ControlerChamps(note, appareils, sources);
        ControlerSeparation(note, implantation.Bandes);

        return new ResultatArmoire
        {
            Note = note, Largeur = largeur, Hauteur = hauteur, Profondeur = profondeur, Cellules = cellules,
            Type = armoire ? "Armoire au sol" : "Coffret mural",
            Appareils = appareils, Bandes = implantation.Bandes, Sources = sources,
            LongueurRail = implantation.Bandes.Count * (largeur - 2 * MargeLaterale) / 1000,
            LongueurGoulotte = (implantation.Bandes.Count + 1) * (largeur - 2 * MargeLaterale) / 1000 + 2 * cellules * hauteur / 1000,
            Occupation = implantation.Occupation,
        };
    }

    static IEnumerable<(double L, double H, int N, bool Armoire)> Candidats()
    {
        foreach (var l in LargeursCoffret)
            foreach (var h in HauteursCoffret)
                yield return (l, h, 1, false);
        foreach (var l in LargeursArmoire)
            foreach (var h in HauteursArmoire)
                for (var n = 1; n <= CellulesMax; n++)
                    yield return (l, h, n, true);
    }

    /// <summary>Appareils montés dans l'armoire, dans l'ordre de l'arbre, plus les borniers estimés.</summary>
    public static List<AppareilImplante> AppareilsArmoire(ResultatProjet r, Projet projet)
    {
        var liste = new List<AppareilImplante>();
        foreach (var e in r.Arbre.Parcourir())
        {
            if (Encombrements.Estimer(e) is not { Largeur: > 0 } enc) continue;
            liste.Add(new AppareilImplante
            {
                Repere = e.Repere, Designation = e.Designation, Famille = e.Famille, Zone = e.Zone, Element = e,
                Largeur = enc.Largeur, Hauteur = enc.Hauteur, Profondeur = enc.Profondeur, DissipationW = enc.DissipationW,
            });
        }

        var puissance = BornesPuissance(r).Sum(b => b.Largeur);
        if (puissance > 0)
            liste.Add(Bornier("X1", "Bornier de puissance", ZoneCem.Sale, puissance));
        var commande = BornesCommande(projet).Nombre * Encombrements.LargeurBorneCommande;
        if (commande > 0)
            liste.Add(Bornier("X2", "Bornier de commande", ZoneCem.Propre, commande));
        return liste;
    }

    static AppareilImplante Bornier(string repere, string designation, ZoneCem zone, double largeur) => new()
    {
        Repere = repere, Designation = designation, Famille = "Bornier", Zone = zone,
        Largeur = largeur, Hauteur = 60, Profondeur = 50,
    };

    /// <summary>Bornes de puissance des départs : phases, neutre et PE, largeur selon la section.</summary>
    public static IEnumerable<(int Nombre, double Section, double Largeur)> BornesPuissance(ResultatProjet r)
    {
        // L'alimentation 24 V est dans l'armoire : son câble ne passe pas par le bornier.
        foreach (var d in r.Departs.Where(d => d.Depart is not DepartAlimentation24V))
        {
            var actifs = d.Depart.Alimentation switch { TypeAlimentation.Mono230 => 2, TypeAlimentation.Tri400 => 3, _ => 4 };
            if (d.Depart is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle }) actifs = 6;
            var nombre = actifs + 1;
            yield return (nombre, d.Section, nombre * Encombrements.LargeurBorne(d.Section));
        }
    }

    /// <summary>Bornes de commande : E/S de l'automate, capteurs de sécurité, distribution 24 V.</summary>
    public static (int Nombre, string Detail) BornesCommande(Projet projet)
    {
        var a = projet.Automate;
        var es = a.Actif ? a.EntreesTor + a.SortiesTor + 2 * (a.EntreesAnalogiques + a.SortiesAnalogiques) : 0;
        var securite = projet.Securite.Fonctions.Sum(f => 2 * Math.Max(1, f.NombreCapteurs));
        var distribution = es + securite > 0 || projet.Departs.OfType<DepartAlimentation24V>().Any() ? 10 : 0;
        return (es + securite + distribution, $"{es} E/S, {securite} sécurité, {distribution} distribution 24 V");
    }

    public record Implantation(bool Tient, List<AppareilImplante> Appareils, List<BandeZone> Bandes, double Occupation);

    /// <summary>Range les appareils par zone sur des rails horizontaux, cellule après cellule.</summary>
    public static Implantation Implanter(IReadOnlyList<AppareilImplante> elements, double largeur, double hauteur, int cellules, double reserve)
    {
        var utile = largeur - 2 * MargeLaterale;
        var capacite = utile * (1 - reserve);
        var places = new List<AppareilImplante>();
        var bandes = new List<BandeZone>();
        var tient = utile > 0;
        var cellule = 0;
        var y = MargeHaute;
        ZoneCem? zonePrecedente = null;

        foreach (var zone in new[] { ZoneCem.Propre, ZoneCem.Intermediaire, ZoneCem.Sale })
        {
            var rangees = Rangees(elements.Where(e => e.Zone == zone).SelectMany(e => Decouper(e, capacite)), capacite);
            foreach (var rangee in rangees)
            {
                var h = rangee.Max(e => e.Hauteur);
                if (zonePrecedente == ZoneCem.Propre && zone == ZoneCem.Sale) y += SeparationCem;
                if (y + h > hauteur - MargeBasse && y > MargeHaute)
                {
                    cellule++;
                    y = MargeHaute;
                }
                if (y + h > hauteur - MargeBasse || rangee.Sum(e => e.Largeur) > utile) tient = false;
                var x0 = cellule * largeur + MargeLaterale;
                var x = x0;
                Guid? depart = null;
                foreach (var e in rangee)
                {
                    if (x > x0 && e.Element?.DepartId != depart) x += JeuDeparts;
                    depart = e.Element?.DepartId;
                    places.Add(Copier(e, x, y + (h - e.Hauteur) / 2));
                    x += e.Largeur;
                }
                bandes.Add(new BandeZone(zone, cellule * largeur, y - EntreRangees / 2, largeur, h + EntreRangees));
                y += h + EntreRangees;
                zonePrecedente = zone;
            }
        }
        if (cellule >= cellules) tient = false;
        var disponible = cellules * (hauteur - MargeHaute - MargeBasse);
        var occupe = cellule * (hauteur - MargeHaute - MargeBasse) + Math.Max(0, y - EntreRangees - MargeHaute);
        return new Implantation(tient, places, bandes, disponible > 0 ? 100 * occupe / disponible : 100);
    }

    /// <summary>Découpe un bornier plus long qu'un rail en tronçons.</summary>
    static IEnumerable<AppareilImplante> Decouper(AppareilImplante e, double capacite)
    {
        if (e.Famille != "Bornier" || e.Largeur <= capacite || capacite <= 0)
        {
            yield return e;
            yield break;
        }
        var troncons = (int)Math.Ceiling(e.Largeur / capacite);
        for (var i = 1; i <= troncons; i++)
            yield return new AppareilImplante
            {
                Repere = $"{e.Repere}.{i}", Designation = $"{e.Designation} ({i}/{troncons})", Famille = e.Famille, Zone = e.Zone,
                Largeur = e.Largeur / troncons, Hauteur = e.Hauteur, Profondeur = e.Profondeur,
            };
    }

    static List<List<AppareilImplante>> Rangees(IEnumerable<AppareilImplante> elements, double capacite)
    {
        var rangees = new List<List<AppareilImplante>>();
        var courante = new List<AppareilImplante>();
        double occupe = 0;
        foreach (var e in elements)
        {
            if (courante.Count > 0 && occupe + JeuDeparts + e.Largeur > capacite)
            {
                rangees.Add(courante);
                courante = new();
                occupe = 0;
            }
            courante.Add(e);
            occupe += e.Largeur + (courante.Count > 1 ? JeuDeparts : 0);
        }
        if (courante.Count > 0) rangees.Add(courante);
        return rangees;
    }

    static AppareilImplante Copier(AppareilImplante e, double x, double y) => new()
    {
        Repere = e.Repere, Designation = e.Designation, Famille = e.Famille, Zone = e.Zone, Element = e.Element,
        X = x, Y = y, Largeur = e.Largeur, Hauteur = e.Hauteur, Profondeur = e.Profondeur, DissipationW = e.DissipationW,
    };

    static void AppliquerPositionsManuelles(List<AppareilImplante> appareils, ConfigurationArmoire config, double largeur, double hauteur)
    {
        foreach (var a in appareils)
        {
            if (!config.Positions.TryGetValue(a.Repere, out var p)) continue;
            a.X = Math.Clamp(p.X, 0, Math.Max(0, largeur - a.Largeur));
            a.Y = Math.Clamp(p.Y, 0, Math.Max(0, hauteur - a.Hauteur));
            a.Manuel = true;
        }
    }

    static void ControlerManuel(NoteDeCalcul note, List<AppareilImplante> appareils, List<BandeZone> bandes, double largeur, double hauteur)
    {
        var manuels = appareils.Where(a => a.Manuel).ToList();
        if (manuels.Count > 0)
            note.Info("Ajustement manuel", $"{manuels.Count} appareil(s) déplacé(s) à la main : {string.Join(", ", manuels.Select(a => a.Repere))}.");

        foreach (var a in manuels)
        {
            var bande = bandes.FirstOrDefault(b => a.CentreX >= b.X && a.CentreX <= b.X + b.Largeur
                                                   && a.CentreY >= b.Y && a.CentreY <= b.Y + b.Hauteur);
            if (bande is null) continue;
            var conflit = (a.Zone, bande.Zone) is (ZoneCem.Sale, ZoneCem.Propre) or (ZoneCem.Propre, ZoneCem.Sale);
            note.Verifier($"Zone de {a.Repere}", !conflit,
                conflit
                    ? $"{a.Designation} ({Libelle(a.Zone)}) est placé en {Libelle(bande.Zone)} : le ramener dans sa zone ou séparer par une tôle reliée à la masse."
                    : $"{a.Designation} placé en {Libelle(bande.Zone)}.",
                RefCem, StatutCalcul.Alerte);
        }

        var chevauchements = new List<string>();
        for (var i = 0; i < appareils.Count; i++)
            for (var j = i + 1; j < appareils.Count; j++)
                if ((appareils[i].Manuel || appareils[j].Manuel) && appareils[i].Chevauche(appareils[j]))
                    chevauchements.Add($"{appareils[i].Repere} / {appareils[j].Repere}");
        if (manuels.Count > 0)
            note.Verifier("Chevauchements", chevauchements.Count == 0,
                chevauchements.Count == 0 ? "Aucun appareil ne se chevauche." : $"Appareils qui se chevauchent : {string.Join(", ", chevauchements)}.",
                RefArmoire, StatutCalcul.Alerte);
    }

    /// <summary>Sources de perturbations et rayon d'influence indicatif selon le type et la puissance.</summary>
    public static List<SourceChamp> Sources(IEnumerable<AppareilImplante> appareils)
    {
        var sources = new List<SourceChamp>();
        foreach (var a in appareils)
        {
            var e = a.Element;
            (double Rayon, double Intensite)? champ = a.Famille switch
            {
                "Variateur" => (120 + 25 * Math.Sqrt(e?.PuissanceKw ?? 1), 1.0),
                "Démarreur progressif" => (80 + 15 * Math.Sqrt(e?.PuissanceKw ?? 1), 0.6),
                "Contacteur" => (50 + 0.5 * (e?.Courant ?? 10), 0.35),
                "Alimentation" => (70, 0.3),
                "Bornier" when a.Zone == ZoneCem.Sale => (60, 0.25),
                _ => null,
            };
            if (champ is { } c)
                sources.Add(new SourceChamp(a.Repere, a.CentreX, a.CentreY, c.Rayon, c.Intensite));
        }
        return sources;
    }

    static void ControlerChamps(NoteDeCalcul note, List<AppareilImplante> appareils, List<SourceChamp> sources)
    {
        var sensibles = appareils.Where(a => a.Zone == ZoneCem.Propre && a.Famille != "Bornier").ToList();
        var exposes = new List<string>();
        foreach (var s in sources.Where(s => s.Intensite >= 0.6))
            foreach (var a in sensibles)
            {
                // Distance du centre de la source au bord le plus proche de l'appareil sensible.
                var dx = Math.Max(Math.Max(a.X - s.X, 0), s.X - (a.X + a.Largeur));
                var dy = Math.Max(Math.Max(a.Y - s.Y, 0), s.Y - (a.Y + a.Hauteur));
                var d = Math.Sqrt(dx * dx + dy * dy);
                if (d < s.Rayon) exposes.Add($"{a.Repere} à {N(d, 0)} mm de {s.Repere}");
            }
        if (sensibles.Count > 0 && sources.Any(s => s.Intensite >= 0.6))
            note.Verifier("Champ des sources de perturbations", exposes.Count == 0,
                exposes.Count == 0
                    ? "Aucun appareil sensible dans le rayon d'influence des variateurs et démarreurs."
                    : $"Appareils sensibles exposés : {string.Join(" ; ", exposes)}. Éloigner, ou séparer par une tôle reliée à la masse.",
                RefCem, StatutCalcul.Alerte);
        if (sources.Any(s => s.Intensite >= 1))
            note.Info("Câblage CEM", "Câbles moteurs blindés, repris à 360° sur la plaque à la sortie ; câbles de puissance et de commande " +
                "dans des goulottes séparées, croisements à 90°.", RefCem);
    }

    static void ControlerSeparation(NoteDeCalcul note, List<BandeZone> bandes)
    {
        var propres = bandes.Where(b => b.Zone == ZoneCem.Propre).ToList();
        var sales = bandes.Where(b => b.Zone == ZoneCem.Sale).ToList();
        if (propres.Count == 0 || sales.Count == 0) return;
        var distance = (from p in propres
                        from s in sales
                        where Math.Abs(p.X - s.X) < 1
                        select Math.Max(s.Y - (p.Y + p.Hauteur), p.Y - (s.Y + s.Hauteur)) + EntreRangees).DefaultIfEmpty(double.MaxValue).Min();
        note.Verifier("Séparation des zones propre et sale", distance >= SeparationCem,
            distance == double.MaxValue
                ? "Zones propre et sale dans des cellules différentes."
                : $"Distance entre zones ≈ {N(distance, 0)} mm (≥ {N(SeparationCem, 0)} mm conseillés).",
            RefCem, StatutCalcul.Alerte);
    }

    public static string Libelle(ZoneCem z) => z switch
    {
        ZoneCem.Sale => "zone sale",
        ZoneCem.Propre => "zone propre",
        ZoneCem.Intermediaire => "zone intermédiaire",
        _ => "hors armoire",
    };
}
