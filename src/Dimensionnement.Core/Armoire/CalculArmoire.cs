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

/// <summary>Bande d'une zone CEM sur la plaque : une rangée de rail et ses goulottes, ou un bloc fixé sur la plaque (variateurs).</summary>
public record BandeZone(ZoneCem Zone, double X, double Y, double Largeur, double Hauteur, bool Rail = true, Guid? Id = null)
{
    /// <summary>Rail ajouté par l'utilisateur ; son axe est au milieu de la bande et il en occupe toute la largeur.</summary>
    public bool Manuel => Id is not null;
}

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

    /// <summary>Enveloppe retenue au catalogue : marque, gamme, références de l'enveloppe et de sa plaque de montage.</summary>
    public string Marque { get; init; } = "";
    public string Gamme { get; init; } = "";
    public string Reference { get; init; } = "";
    public string ReferencePlaque { get; init; } = "";

    /// <summary>Surface utile de la plaque de montage d'une cellule, en mm, et son décalage dans l'enveloppe.</summary>
    public double PlaqueLargeur { get; init; }
    public double PlaqueHauteur { get; init; }
    public double PlaqueX => (Largeur - PlaqueLargeur) / 2;
    public double PlaqueY => (Hauteur - PlaqueHauteur) / 2;
    public IReadOnlyList<AppareilImplante> Appareils { get; init; } = Array.Empty<AppareilImplante>();
    public IReadOnlyList<BandeZone> Bandes { get; init; } = Array.Empty<BandeZone>();
    public IReadOnlyList<SourceChamp> Sources { get; init; } = Array.Empty<SourceChamp>();

    /// <summary>Champ magnétique reçu par chaque appareil sensible de la zone propre, en A/m.</summary>
    public IReadOnlyDictionary<string, double> Expositions { get; init; } = new Dictionary<string, double>();

    /// <summary>Longueur de rail DIN et de goulotte nécessaires, en m (pour la liste de matériel).</summary>
    public double LongueurRail { get; init; }
    public double LongueurGoulotte { get; init; }

    /// <summary>Fils estimés et goulottes implantées.</summary>
    public ResultatCablage Cablage { get; init; } = new();

    /// <summary>Largeur réservée aux goulottes verticales de chaque côté de la plaque, en mm.</summary>
    public double Laterale { get; init; } = CalculArmoire.MargeLaterale;

    /// <summary>Hauteur de plaque occupée, en % de la hauteur disponible de toutes les cellules.</summary>
    public double Occupation { get; init; }

    /// <summary>Vrai si la taille est imposée par l'utilisateur.</summary>
    public bool TailleImposee { get; init; }

    /// <summary>Réserve réellement laissée, en % (réduite quand la taille imposée l'exige).</summary>
    public double ReserveEffective { get; init; }

    /// <summary>Faux si le matériel ne tient pas dans l'enveloppe ; Alerte donne alors la raison et la taille nécessaire.</summary>
    public bool Tient { get; init; } = true;
    public string Alerte { get; init; } = "";
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
    /// <summary>Dégagement au-dessus et au-dessous des variateurs pour leur ventilation (notices fabricants), en mm.</summary>
    public const double DegagementVariateur = 100;
    /// <summary>Jeu entre deux variateurs montés côte à côte, en mm.</summary>
    const double JeuVariateurs = 10;
    /// <summary>Goulotte verticale entre le bloc des variateurs et les rails de la zone sale, en mm.</summary>
    public const double GoulotteVariateurs = 60;

    const int CellulesMax = 8;

    public static ResultatArmoire Calculer(ResultatProjet r, Projet projet, IReadOnlyList<ModeleEnveloppe>? enveloppes = null)
    {
        var config = projet.Armoire;
        var note = new NoteDeCalcul("ARM", config.Forcages);
        var elements = AppareilsArmoire(r, projet);
        var reserve = Math.Clamp(config.ReservePourcent, 0, 80) / 100;
        var catalogue = enveloppes ?? CatalogueEnveloppes.Charger();
        var marque = config.Marque;
        var modeles = CatalogueEnveloppes.DeLaMarque(marque, catalogue).ToList();

        // Plus petite enveloppe du catalogue de la marque qui reçoit tout le matériel avec la réserve demandée.
        var formats = modeles.GroupBy(m => (m.Largeur, m.Hauteur, m.Type)).Select(g => g.First()).ToList();
        var candidats = formats.Where(f => !f.EstArmoire).Select(f => (M: f, N: 1))
            .Concat(formats.Where(f => f.EstArmoire).SelectMany(f => Enumerable.Range(1, CellulesMax).Select(n => (M: f, N: n))))
            .OrderBy(c => c.M.Largeur * c.N * c.M.Hauteur).ThenBy(c => c.M.Hauteur).ToList();
        // Premier passage avec les goulottes par défaut : estimation des fils, puis largeur de goulotte à réserver.
        var gabarit = Gabarit.Defaut;
        var essai = candidats.FirstOrDefault(c => Implanter(elements, c.M, c.N, reserve, gabarit).Tient);
        if (essai.M is not null)
        {
            var premier = Implanter(elements, essai.M, essai.N, reserve, gabarit);
            gabarit = CalculCablage.Estimer(r, premier.Appareils, premier.Bandes, essai.M.Largeur, essai.M.PlaqueLargeur,
                essai.M.PlaqueHauteur, essai.M.Hauteur, essai.N, gabarit).Gabarit;
        }
        var trouve = candidats.FirstOrDefault(c => Implanter(elements, c.M, c.N, reserve, gabarit).Tient);
        var auto = trouve.M is null
            ? (L: 1200.0, H: 2000.0, N: CellulesMax)
            : (L: trouve.M.Largeur, H: trouve.M.Hauteur, N: trouve.N);

        var largeurs = elements.Count == 0 ? 0 : elements.Sum(e => e.Largeur);
        var entrees = $"{elements.Count} appareil(s) et bornier(s), {N(largeurs / 1000)} m de rail, réserve {N(config.ReservePourcent)} %";
        double largeur, hauteur;
        int cellules;
        var imposee = config.TailleImposee;
        if (imposee)
        {
            largeur = Math.Max(200, config.LargeurImposee);
            hauteur = Math.Max(200, config.HauteurImposee);
            cellules = Math.Max(1, config.CellulesImposees);
            note.Info("Taille imposée", $"{N(largeur)} × {N(hauteur)} × {N(config.ProfondeurImposee)} mm" +
                (cellules > 1 ? $", {cellules} cellules" : "") + $" ; la plus petite enveloppe qui convient serait {N(auto.L)} × {N(auto.H)} mm" +
                (auto.N > 1 ? $" × {auto.N} cellules" : "") + ".", RefArmoire);
        }
        else
        {
            largeur = note.Valeur(CodeLargeur, "Largeur d'une cellule", auto.L, auto.L, "mm",
                $"Plus petite enveloppe {marque} du catalogue qui reçoit le matériel", entrees, RefArmoire);
            hauteur = note.Valeur(CodeHauteur, "Hauteur", auto.H, auto.H, "mm",
                "Rangées par zone CEM + goulottes + marges sur la plaque de montage", entrees, RefArmoire);
            cellules = (int)Math.Max(1, Math.Round(note.Valeur(CodeCellules, "Nombre de cellules juxtaposées", auto.N, auto.N, "",
                "Cellules supplémentaires quand la hauteur ne suffit plus", "", RefArmoire)));
        }
        var armoire = hauteur >= 1600;

        var profondeurAppareils = elements.Count == 0 ? 0 : elements.Max(e => e.Profondeur);
        var profondeurMini = profondeurAppareils + 100;
        var profondeurs = modeles.Where(m => Math.Abs(m.Largeur - largeur) < 1 && Math.Abs(m.Hauteur - hauteur) < 1)
            .Select(m => m.Profondeur).Distinct().OrderBy(p => p).ToArray();
        if (profondeurs.Length == 0) profondeurs = armoire ? new double[] { 400, 500, 600, 800 } : new double[] { 200, 250, 300, 400 };
        var profondeurAuto = Calibres.Superieur(profondeurs, profondeurMini) ?? profondeurs[^1];
        var profondeur = imposee
            ? config.ProfondeurImposee
            : note.Valeur(CodeProfondeur, "Profondeur", profondeurMini, profondeurAuto, "mm",
                "P ≥ profondeur du plus gros appareil + 100 mm (plaque, porte, câblage)",
                $"appareil le plus profond : {N(profondeurAppareils)} mm", RefArmoire);
        note.Verifier("Profondeur", profondeur >= profondeurMini,
            $"Profondeur {N(profondeur)} mm pour {N(profondeurMini)} mm nécessaires.", RefArmoire,
            imposee ? StatutCalcul.NonConforme : StatutCalcul.Alerte);

        var enveloppe = CatalogueEnveloppes.Trouver(marque, largeur, hauteur, profondeur, catalogue);

        // Taille imposée : la réserve est réduite par pas de 5 % jusqu'à 0 pour essayer de tout faire entrer.
        var implantation = Implanter(elements, enveloppe, cellules, reserve, gabarit);
        var reserveEffective = reserve;
        while (imposee && !implantation.Tient && reserveEffective > 0)
        {
            reserveEffective = Math.Max(0, reserveEffective - 0.05);
            implantation = Implanter(elements, enveloppe, cellules, reserveEffective, gabarit);
        }
        var alerte = "";
        if (!implantation.Tient)
            alerte = imposee
                ? $"Impossible de tout faire entrer dans l'armoire imposée {N(largeur)} × {N(hauteur)} mm" +
                  (cellules > 1 ? $" × {cellules} cellules" : "") + $", même sans réserve. Taille nécessaire : {N(auto.L)} × {N(auto.H)} mm" +
                  (auto.N > 1 ? $" × {auto.N} cellules" : "") + $" avec {N(config.ReservePourcent)} % de réserve."
                : "Le matériel ne tient pas dans les dimensions retenues : agrandir l'armoire ou réduire la réserve.";
        else if (profondeur < profondeurMini && imposee)
            alerte = $"Profondeur imposée {N(profondeur)} mm insuffisante : {N(profondeurMini)} mm nécessaires pour l'appareil le plus profond.";
        else if (reserveEffective < reserve - 1e-6)
            alerte = $"Le matériel ne tient dans l'armoire imposée qu'en réduisant la réserve à {N(reserveEffective * 100)} % " +
                     $"au lieu de {N(config.ReservePourcent)} %.";
        note.Info("Enveloppe", $"{(armoire ? "Armoire au sol" : "Coffret mural")} {marque} {enveloppe.Gamme} {N(largeur)} × {N(hauteur)} × {N(profondeur)} mm" +
            (cellules > 1 ? $", {cellules} cellules juxtaposées (largeur totale {N(largeur * cellules)} mm)" : "") +
            $" : {enveloppe.Reference} ; plaque : {enveloppe.ReferencePlaque}, surface utile {N(enveloppe.PlaqueLargeur)} × {N(enveloppe.PlaqueHauteur)} mm" +
            (enveloppe.Verifie ? "." : " (références indicatives, à vérifier)."), "Catalogues Rittal, Legrand, Schneider ; " + RefArmoire);
        note.Verifier("Implantation", implantation.Tient && reserveEffective >= reserve - 1e-6,
            implantation.Tient && reserveEffective >= reserve - 1e-6
                ? $"Tout le matériel tient avec {N(config.ReservePourcent)} % de réserve ; hauteur occupée {Pct(implantation.Occupation)}."
                : alerte,
            RefArmoire, implantation.Tient ? StatutCalcul.Alerte : StatutCalcul.NonConforme);
        note.Info("Zones CEM", "Zone propre en haut (automate, E/S, sécurité), zone intermédiaire (protections, alimentations), " +
            "zone sale en bas, au plus près des presse-étoupes des câbles moteurs : contacteurs et borniers sur rails, " +
            $"variateurs et démarreurs regroupés côte à côte sur le côté, fixés sur la plaque avec {N(DegagementVariateur, 0)} mm de dégagement " +
            "en haut et en bas pour la ventilation.", RefCem);

        var appareils = implantation.Appareils;
        AppliquerPositionsManuelles(appareils, config, largeur * cellules, hauteur);
        var bandes = RecalculerZones(implantation.Bandes, appareils, config.Rails, largeur * cellules, hauteur);
        ControlerManuel(note, appareils, bandes, largeur * cellules, hauteur);

        var cablage = CalculCablage.Estimer(r, appareils, implantation.Bandes, largeur, enveloppe.PlaqueLargeur, enveloppe.PlaqueHauteur,
            hauteur, cellules, gabarit);
        cablage = new ResultatCablage
        {
            Fils = cablage.Fils, Horizontale = cablage.Horizontale, Verticale = cablage.Verticale, Gabarit = cablage.Gabarit,
            LongueurMoyenne = cablage.LongueurMoyenne,
            Goulottes = cablage.Goulottes.Concat(config.Goulottes.Select(g => new GoulottePlacee(g.X, g.Y,
                g.Verticale ? g.Largeur : g.Longueur, g.Verticale ? g.Longueur : g.Largeur, g.Verticale,
                new ModeleGoulotte(g.Largeur, g.Hauteur), 0, 0, g.Id))).ToList(),
        };
        CalculCablage.Noter(note, cablage, appareils.Count(a => a.Element is not null));

        var sources = ChampMagnetique.Sources(appareils);
        var expositions = ControlerChamps(note, appareils, sources);
        ControlerSeparation(note, bandes);

        return new ResultatArmoire
        {
            Note = note, Largeur = largeur, Hauteur = hauteur, Profondeur = profondeur, Cellules = cellules,
            Type = armoire ? "Armoire au sol" : "Coffret mural",
            Appareils = appareils, Bandes = bandes, Sources = sources, Expositions = expositions,
            LongueurRail = (implantation.Bandes.Where(b => b.Rail).Sum(b => b.Largeur - 2 * gabarit.Laterale) + config.Rails.Sum(x => x.Longueur)) / 1000,
            LongueurGoulotte = cablage.Goulottes.Sum(x => x.Verticale ? x.Hauteur : x.Largeur) / 1000,
            Cablage = cablage, Laterale = gabarit.Laterale,
            Marque = marque.ToString(), Gamme = enveloppe.Gamme, Reference = enveloppe.Reference, ReferencePlaque = enveloppe.ReferencePlaque,
            PlaqueLargeur = enveloppe.PlaqueLargeur, PlaqueHauteur = enveloppe.PlaqueHauteur,
            Occupation = implantation.Occupation,
            TailleImposee = imposee, ReserveEffective = reserveEffective * 100,
            Tient = implantation.Tient && !(imposee && profondeur < profondeurMini), Alerte = alerte,
        };
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
            var actifs = d.Depart.Alimentation switch { TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => 2, TypeAlimentation.Tri400 => 3, _ => 4 };
            if (d.Depart is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle }) actifs = 6;
            var nombre = actifs + 1;
            yield return (nombre, d.Section, nombre * Encombrements.LargeurBorne(d.Section));
        }
    }

    /// <summary>Bornes de commande : E/S de l'automate, capteurs de sécurité, distribution 24 V.</summary>
    public static (int Nombre, string Detail) BornesCommande(Projet projet)
    {
        var a = projet.Automate;
        var es = a.Actif ? a.EntreesTor + a.SortiesTor + 2 * (a.EntreesAnalogiques + a.SortiesAnalogiques)
            + a.Stations.Where(x => x.DansArmoire).Sum(x => x.EntreesTor + x.SortiesTor + 2 * (x.EntreesAnalogiques + x.SortiesAnalogiques)) : 0;
        var securite = projet.Securite.Fonctions.Sum(f => 2 * Math.Max(1, f.NombreCapteurs));
        var distribution = es + securite > 0 || projet.Departs.OfType<DepartAlimentation24V>().Any() ? 10 : 0;
        return (es + securite + distribution, $"{es} E/S, {securite} sécurité, {distribution} distribution 24 V");
    }

    public record Implantation(bool Tient, List<AppareilImplante> Appareils, List<BandeZone> Bandes, double Occupation);

    /// <summary>Famille d'appareils de puissance électronique regroupés à part, fixés sur la plaque.</summary>
    public static bool EstVariateur(AppareilImplante a) => a.Famille is "Variateur" or "Démarreur progressif";

    /// <summary>
    /// Range les appareils par zone sur des rails horizontaux, cellule après cellule. Dans la zone sale, les variateurs
    /// et démarreurs progressifs sont regroupés côte à côte sur le côté droit, fixés sur la plaque avec leur dégagement
    /// de ventilation, au plus près des presse-étoupes des câbles moteurs ; contacteurs et borniers occupent les rails à gauche.
    /// </summary>
    public static Implantation Implanter(IReadOnlyList<AppareilImplante> elements, ModeleEnveloppe enveloppe, int cellules, double reserve,
        Gabarit? gabarit = null)
    {
        var g = gabarit ?? Gabarit.Defaut;
        var margeLaterale = g.Laterale;
        var entreRangees = g.EntreRangees;
        // Coordonnées dans l'enveloppe ; le matériel est posé sur la plaque de montage, centrée dans chaque cellule.
        var largeur = enveloppe.Largeur;
        var plaque = enveloppe.PlaqueLargeur;
        var dx = (largeur - plaque) / 2;
        var haut = (enveloppe.Hauteur - enveloppe.PlaqueHauteur) / 2 + MargeHaute;
        var bas = (enveloppe.Hauteur + enveloppe.PlaqueHauteur) / 2 - MargeBasse;
        var utile = plaque - 2 * margeLaterale;
        var capacite = utile * (1 - reserve);
        var places = new List<AppareilImplante>();
        var bandes = new List<BandeZone>();
        var tient = utile > 0;
        var cellule = 0;
        var y = haut;
        ZoneCem? zonePrecedente = null;

        // Passe à la cellule suivante si un bloc de hauteur h ne tient plus dans la cellule courante.
        void Reserver(double h)
        {
            if (y + h > bas && y > haut)
            {
                cellule++;
                y = haut;
            }
            if (y + h > bas) tient = false;
        }

        // Pose une rangée à partir de x0 ; renvoie sa hauteur.
        double Poser(List<AppareilImplante> rangee, double x0, double yRangee, double jeu, bool parDepart)
        {
            var h = rangee.Max(e => e.Hauteur);
            var x = x0;
            Guid? depart = null;
            foreach (var e in rangee)
            {
                if (x > x0 && (!parDepart || e.Element?.DepartId != depart)) x += jeu;
                depart = e.Element?.DepartId;
                places.Add(Copier(e, x, yRangee + (h - e.Hauteur) / 2));
                x += e.Largeur;
            }
            return h;
        }

        foreach (var zone in new[] { ZoneCem.Propre, ZoneCem.Intermediaire, ZoneCem.Sale })
        {
            var appareils = elements.Where(e => e.Zone == zone).ToList();
            if (appareils.Count == 0) continue;
            if (zonePrecedente == ZoneCem.Propre && zone == ZoneCem.Sale) y += SeparationCem;
            zonePrecedente = zone;

            var variateurs = zone == ZoneCem.Sale ? appareils.Where(EstVariateur).ToList() : new List<AppareilImplante>();
            var surRail = appareils.Except(variateurs).SelectMany(e => Decouper(e, capacite)).ToList();

            // Bloc des variateurs sur le côté droit si les rails gardent au moins 40 % de la largeur ; sinon en rangées pleines en bas de zone.
            var rangeesVariateurs = Rangees(variateurs, utile, JeuVariateurs);
            var largeurBloc = rangeesVariateurs.Select(r => r.Sum(e => e.Largeur) + (r.Count - 1) * JeuVariateurs).DefaultIfEmpty(0).Max();
            var aCote = variateurs.Count > 0 && surRail.Count > 0 && utile - largeurBloc - GoulotteVariateurs >= 0.4 * utile;
            var largeurRails = aCote ? utile - largeurBloc - GoulotteVariateurs : utile;
            var rangeesRail = Rangees(surRail, largeurRails * (1 - reserve), JeuDeparts);
            if (rangeesRail.Any(r => r.Sum(e => e.Largeur) > largeurRails)) tient = false;

            double HauteurRail(IEnumerable<List<AppareilImplante>> r) => r.Sum(x => x.Max(e => e.Hauteur) + entreRangees);
            double HauteurBloc(IEnumerable<List<AppareilImplante>> r) => r.Sum(x => x.Max(e => e.Hauteur) + DegagementVariateur) + DegagementVariateur - entreRangees;

            if (aCote)
            {
                // Rails à gauche et bloc des variateurs à droite partagent la même hauteur de zone.
                var h = Math.Max(HauteurRail(rangeesRail), HauteurBloc(rangeesVariateurs));
                Reserver(h - entreRangees);
                var x0 = cellule * largeur + dx + margeLaterale;
                var yRail = y;
                foreach (var rangee in rangeesRail)
                {
                    var hr = Poser(rangee, x0, yRail, JeuDeparts, true);
                    bandes.Add(new BandeZone(zone, cellule * largeur + dx, yRail - entreRangees / 2, margeLaterale + largeurRails + GoulotteVariateurs / 2, hr + entreRangees));
                    yRail += hr + entreRangees;
                }
                var xBloc = x0 + largeurRails + GoulotteVariateurs;
                var yBloc = y - entreRangees / 2 + DegagementVariateur;
                foreach (var rangee in rangeesVariateurs)
                    yBloc += Poser(rangee, xBloc + largeurBloc - (rangee.Sum(e => e.Largeur) + (rangee.Count - 1) * JeuVariateurs), yBloc, JeuVariateurs, false)
                             + DegagementVariateur;
                bandes.Add(new BandeZone(zone, xBloc - GoulotteVariateurs / 2, y - entreRangees / 2,
                    cellule * largeur + dx + plaque - (xBloc - GoulotteVariateurs / 2), h, false));
                y += h;
                continue;
            }

            foreach (var rangee in rangeesRail)
            {
                var hr = rangee.Max(e => e.Hauteur);
                Reserver(hr);
                Poser(rangee, cellule * largeur + dx + margeLaterale, y, JeuDeparts, true);
                bandes.Add(new BandeZone(zone, cellule * largeur + dx, y - entreRangees / 2, plaque, hr + entreRangees));
                y += hr + entreRangees;
            }
            foreach (var rangee in rangeesVariateurs)
            {
                var hr = rangee.Max(e => e.Hauteur) + 2 * DegagementVariateur - entreRangees;
                Reserver(hr);
                var yr = y - entreRangees / 2 + DegagementVariateur;
                Poser(rangee, cellule * largeur + dx + margeLaterale, yr, JeuVariateurs, false);
                bandes.Add(new BandeZone(zone, cellule * largeur + dx, y - entreRangees / 2, plaque, hr + entreRangees, false));
                y += hr + entreRangees;
            }
        }
        if (cellule >= cellules) tient = false;
        var disponible = cellules * (bas - haut);
        var occupe = cellule * (bas - haut) + Math.Max(0, y - entreRangees - haut);
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

    static List<List<AppareilImplante>> Rangees(IEnumerable<AppareilImplante> elements, double capacite, double jeu)
    {
        var rangees = new List<List<AppareilImplante>>();
        var courante = new List<AppareilImplante>();
        double occupe = 0;
        foreach (var e in elements)
        {
            if (courante.Count > 0 && occupe + jeu + e.Largeur > capacite)
            {
                rangees.Add(courante);
                courante = new();
                occupe = 0;
            }
            courante.Add(e);
            occupe += e.Largeur + (courante.Count > 1 ? jeu : 0);
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

    /// <summary>Appareils d'une bande : ceux dont le centre est dans son rectangle.</summary>
    public static IEnumerable<AppareilImplante> Dans(BandeZone b, IEnumerable<AppareilImplante> appareils) =>
        appareils.Where(a => a.CentreX >= b.X && a.CentreX <= b.X + b.Largeur && a.CentreY >= b.Y && a.CentreY <= b.Y + b.Hauteur);

    /// <summary>
    /// Zones après les déplacements manuels : chaque bande (rangée automatique ou rail ajouté) prend le niveau d'émission
    /// le plus fort des appareils qu'elle porte (sale, puis intermédiaire, puis propre) ; une bande vide garde son niveau prévu.
    /// Un rail ajouté reçoit une bande à la hauteur des appareils posés dessus.
    /// </summary>
    public static List<BandeZone> RecalculerZones(IReadOnlyList<BandeZone> bandes, IReadOnlyList<AppareilImplante> appareils,
        IEnumerable<RailManuel> rails, double largeur, double hauteur)
    {
        static ZoneCem? Niveau(IEnumerable<AppareilImplante> sur) =>
            sur.Where(a => a.Famille != "Bornier" && a.Zone != ZoneCem.HorsArmoire).Select(a => (ZoneCem?)a.Zone).Min();
        var resultat = bandes.Select(b => Niveau(Dans(b, appareils)) is { } z && z != b.Zone ? b with { Zone = z } : b).ToList();
        foreach (var rail in rails)
        {
            var x = Math.Clamp(rail.X, 0, largeur);
            var longueur = Math.Min(rail.Longueur, largeur - x);
            var sur = appareils.Where(a => a.CentreX >= x && a.CentreX <= x + longueur
                                           && a.Y - 10 <= rail.Y && a.Y + a.Hauteur + 10 >= rail.Y).ToList();
            var h = Math.Max(120, sur.Select(a => 2 * Math.Max(rail.Y - a.Y, a.Y + a.Hauteur - rail.Y)).DefaultIfEmpty(0).Max() + 30);
            resultat.Add(new BandeZone(Niveau(sur) ?? ZoneCem.Intermediaire, x, Math.Clamp(rail.Y, 0, hauteur) - h / 2, longueur, h, true, rail.Id));
        }
        return resultat;
    }

    static void ControlerManuel(NoteDeCalcul note, List<AppareilImplante> appareils, List<BandeZone> bandes, double largeur, double hauteur)
    {
        var manuels = appareils.Where(a => a.Manuel).ToList();
        if (manuels.Count > 0)
            note.Info("Ajustement manuel", $"{manuels.Count} appareil(s) déplacé(s) à la main : {string.Join(", ", manuels.Select(a => a.Repere))}. " +
                "Les zones sont recalculées d'après les appareils posés sur chaque rail.");
        var railsManuels = bandes.Count(b => b.Manuel);
        if (railsManuels > 0)
            note.Info("Rails ajoutés à la main", $"{railsManuels} rail(s) DIN : " +
                string.Join(", ", bandes.Where(b => b.Manuel).Select(b => $"{N(b.Largeur, 0)} mm en {Libelle(b.Zone)}")) + ".");

        // Une même bande ne doit pas mêler sources de perturbations et appareils sensibles.
        foreach (var b in bandes)
        {
            var sur = Dans(b, appareils).ToList();
            var sales = sur.Where(a => a.Zone == ZoneCem.Sale).ToList();
            var propres = sur.Where(a => a.Zone == ZoneCem.Propre && a.Famille != "Bornier").ToList();
            if (sales.Count == 0 || propres.Count == 0) continue;
            note.Verifier($"Mélange de zones ({string.Join(", ", propres.Select(a => a.Repere))})", false,
                $"{string.Join(", ", propres.Select(a => a.Repere))} (sensible) à côté de {string.Join(", ", sales.Select(a => a.Repere))} (source) " +
                "sur la même rangée : la rangée passe en zone sale. Déplacer, ou séparer par une tôle reliée à la masse.",
                RefCem, StatutCalcul.Alerte);
        }
        foreach (var a in manuels.Where(a => !EstVariateur(a)))
            if (!bandes.Any(b => b.Rail && Dans(b, new[] { a }).Any()))
                note.Verifier($"Rail de {a.Repere}", false, $"{a.Designation} n'est sur aucun rail DIN : ajouter un rail sous l'appareil.",
                    RefArmoire, StatutCalcul.Alerte);

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

    /// <summary>
    /// Champ magnétique reçu par les appareils sensibles (automate, E/S, sécurité) comparé à l'immunité industrielle.
    /// </summary>
    static Dictionary<string, double> ControlerChamps(NoteDeCalcul note, List<AppareilImplante> appareils, List<SourceChamp> sources)
    {
        var expositions = new Dictionary<string, double>();
        if (sources.Count == 0) return expositions;
        note.Info("Champ magnétique", "Chaque source est assimilée à un faisceau de conducteurs d'écart e : H = k × I × e / (2π × d²), contributions sommées (majorant). " +
            "k = 3 variateur et alimentation à découpage (harmoniques, commutation), 2 démarreur progressif, 1 contacteur ; " +
            $"e = √3 × {N(ChampMagnetique.EntraxePoles, 0)} mm en triphasé.",
            ChampMagnetique.Reference);
        foreach (var s in sources.OrderByDescending(s => s.Equivalent).Take(4))
            note.Info($"Source {s.Repere}", $"{s.Famille} : I = {N(s.Courant, 1)} A, k = {N(s.Facteur, 1)} ; " +
                $"{N(ChampMagnetique.ImmuniteIndustrielle, 0)} A/m jusqu'à {N(s.Rayon(ChampMagnetique.ImmuniteIndustrielle), 0)} mm, " +
                $"10 A/m jusqu'à {N(s.Rayon(10), 0)} mm.", ChampMagnetique.Reference);

        var exposes = new List<string>();
        var pire = (Repere: "", H: 0.0);
        foreach (var a in appareils.Where(a => a.Zone == ZoneCem.Propre && a.Famille != "Bornier"))
        {
            var h = ChampMagnetique.Exposition(sources, a, out var principale);
            expositions[a.Repere] = h;
            if (h > pire.H) pire = (a.Repere, h);
            if (h > ChampMagnetique.ImmuniteIndustrielle)
                exposes.Add($"{a.Repere} reçoit {N(h, 0)} A/m, surtout de {principale?.Repere}");
        }
        if (expositions.Count > 0)
            note.Verifier("Champ des sources de perturbations", exposes.Count == 0,
                exposes.Count == 0
                    ? $"Champ maximal sur les appareils sensibles : {N(pire.H, 1)} A/m ({pire.Repere}), " +
                      $"≤ {N(ChampMagnetique.ImmuniteIndustrielle, 0)} A/m d'immunité industrielle."
                    : $"{string.Join(" ; ", exposes)} (> {N(ChampMagnetique.ImmuniteIndustrielle, 0)} A/m). " +
                      "Éloigner, ou séparer par une tôle reliée à la masse.",
                ChampMagnetique.Reference, StatutCalcul.Alerte);
        if (sources.Any(s => s.Famille == "Variateur"))
            note.Info("Câblage CEM", "Câbles moteurs blindés, repris à 360° sur la plaque à la sortie ; câbles de puissance et de commande " +
                "dans des goulottes séparées, croisements à 90°.", RefCem);
        return expositions;
    }

    static void ControlerSeparation(NoteDeCalcul note, List<BandeZone> bandes)
    {
        var propres = bandes.Where(b => b.Zone == ZoneCem.Propre).ToList();
        var sales = bandes.Where(b => b.Zone == ZoneCem.Sale).ToList();
        if (propres.Count == 0 || sales.Count == 0) return;
        var distance = (from p in propres
                        from s in sales
                        where p.X < s.X + s.Largeur - 1 && s.X < p.X + p.Largeur - 1
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
