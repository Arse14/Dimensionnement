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

    /// <summary>Clé stable des positions manuelles : le repère calculé, même si l'utilisateur l'a renommé.</summary>
    public string Cle => Element?.RepereOrigine ?? Repere;
    public double X { get; set; }
    public double Y { get; set; }
    public double Largeur { get; init; }
    public double Hauteur { get; init; }
    public double Profondeur { get; init; }
    public double DissipationW { get; init; }

    /// <summary>Origine des cotes (fiche du modèle, gamme relevée, estimation) et si elles sont vérifiées.</summary>
    public string Origine { get; init; } = "";
    public bool CotesVerifiees { get; init; }

    /// <summary>Vrai si l'utilisateur a déplacé l'appareil à la main.</summary>
    public bool Manuel { get; set; }

    public double CentreX => X + Largeur / 2;
    public double CentreY => Y + Hauteur / 2;

    /// <summary>
    /// Vrai si les deux appareils empiètent l'un sur l'autre au-delà du pas de déplacement à la main (5 mm) : deux appareils
    /// posés côte à côte au pas de 5 mm, dont la largeur n'en est pas un multiple (17,5 mm), se touchent sans se chevaucher.
    /// </summary>
    public bool Chevauche(AppareilImplante a) =>
        X < a.X + a.Largeur - CalculArmoire.PasManuel && a.X < X + Largeur - CalculArmoire.PasManuel
        && Y < a.Y + a.Hauteur - CalculArmoire.PasManuel && a.Y < Y + Hauteur - CalculArmoire.PasManuel;
}

/// <summary>Bande d'une zone CEM sur la plaque : une rangée de rail et ses goulottes, ou un bloc fixé sur la plaque (variateurs).</summary>
public record BandeZone(ZoneCem Zone, double X, double Y, double Largeur, double Hauteur, bool Rail = true, Guid? Id = null, bool Bornier = false)
{
    /// <summary>Rail ajouté par l'utilisateur ; son axe est au milieu de la bande et il en occupe toute la largeur.</summary>
    public bool Manuel => Id is not null;
}

public class ResultatArmoire
{
    public required NoteDeCalcul Note { get; init; }

    /// <summary>« Armoire principale » ou nom du coffret déporté ; Id est celui du coffret (vide pour l'armoire principale).</summary>
    public string Nom { get; set; } = "Armoire principale";
    public Guid? Id { get; set; }

    /// <summary>Coffrets déportés, portés par l'armoire principale.</summary>
    public IReadOnlyList<ResultatArmoire> Coffrets { get; set; } = Array.Empty<ResultatArmoire>();

    /// <summary>Bilan thermique de cette enveloppe.</summary>
    public ResultatThermique? Thermique { get; set; }

    /// <summary>Sectionneur général monté hors plaque (en porte ou sur le côté) et son montage ; null s'il est sur la plaque.</summary>
    public ElementMateriel? SectionneurHorsPlaque { get; set; }
    public MontageSectionneur MontageSectionneur { get; set; }

    /// <summary>Le dégagement des variateurs n'est tenu qu'avec une ventilation forcée : le bilan thermique l'impose.</summary>
    public bool VentilationForcee { get; set; }
    public double DegagementRetenu { get; set; } = CalculArmoire.DegagementVariateur;

    /// <summary>L'armoire principale puis ses coffrets déportés.</summary>
    public IEnumerable<ResultatArmoire> Enveloppes => Coffrets.Prepend(this);

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

    /// <summary>IHM encastrées dans la porte, en coordonnées de l'armoire vue de face.</summary>
    public IReadOnlyList<AppareilImplante> Porte { get; init; } = Array.Empty<AppareilImplante>();

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

    /// <summary>Implantation libre : la disposition est entièrement celle de l'utilisateur (rails, goulottes, appareils).</summary>
    public bool Libre { get; init; }

    /// <summary>Profondeur nécessaire au matériel le plus profond (et à l'IHM en porte), en mm.</summary>
    public double ProfondeurNecessaire { get; init; }

    /// <summary>Vrai si la taille est imposée par l'utilisateur.</summary>
    public bool TailleImposee { get; init; }

    /// <summary>Réserve réellement laissée, en % (réduite quand la taille imposée l'exige).</summary>
    public double ReserveEffective { get; init; }

    /// <summary>Largeur de la zone « à placer » à droite de l'armoire, où attendent les appareils qui n'y entrent pas, en mm.</summary>
    public double LargeurAttente { get; init; }

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

    /// <summary>Pas de déplacement des appareils à la main, en mm : tolérance des contrôles de chevauchement et de plaque.</summary>
    public const double PasManuel = 5;
    /// <summary>Dégagement au-dessus et au-dessous des variateurs pour leur ventilation (notices fabricants), en mm.</summary>
    public const double DegagementVariateur = 100;
    /// <summary>Jeu entre deux variateurs montés côte à côte, en mm.</summary>
    const double JeuVariateurs = 10;
    /// <summary>Goulotte verticale entre le bloc des variateurs et les rails de la zone sale, en mm.</summary>
    public const double GoulotteVariateurs = 60;

    const int CellulesMax = 8;

    /// <summary>
    /// Armoire principale et coffrets déportés : chaque appareil affecté à un coffret existant y est implanté, le reste
    /// va dans l'armoire principale ; chaque enveloppe est dimensionnée sur son seul matériel.
    /// </summary>
    public static ResultatArmoire Calculer(ResultatProjet r, Projet projet, IReadOnlyList<ModeleEnveloppe>? enveloppes = null)
    {
        var config = projet.Armoire;
        var tous = AppareilsArmoire(r, projet);
        Guid? Coffret(AppareilImplante a) => a.Element?.CoffretId is { } impose ? impose :
            config.Affectations.TryGetValue(CleAffectation(a), out var id) && config.Coffrets.Any(c => c.Id == id) ? id : null;
        // Les IHM en porte sont sur la porte de l'armoire principale.
        var porte = r.Arbre.Parcourir().Where(e => e.EnPorte).ToList();
        var principale = Calculer(r, tous.Where(a => Coffret(a) is null).ToList(), config, enveloppes, porte);
        principale.Nom = "Armoire principale";
        if (r.Arbre.Parcourir().FirstOrDefault(e => e.RepereOrigine == "QS0" && e.SurParoi) is { } qs)
        {
            principale.SectionneurHorsPlaque = qs;
            principale.MontageSectionneur = config.MontageSectionneur;
            principale.Note.Groupe = "Protection générale";
            principale.Note.Info("Sectionneur général", $"{qs.Repere} monté " + (config.MontageSectionneur == MontageSectionneur.Porte
                ? "en façade de porte : pas de place sur la plaque, hors zones CEM ; ses pertes restent dans l'armoire."
                : "sur le côté de l'armoire, à l'extérieur : pas de place sur la plaque, hors zones CEM et hors bilan thermique."), RefArmoire);
        }
        principale.Coffrets = config.Coffrets.Select(c =>
        {
            var coffret = Calculer(r, tous.Where(a => Coffret(a) == c.Id).ToList(), c, enveloppes, Array.Empty<ElementMateriel>());
            coffret.Id = c.Id;
            coffret.Nom = c.Nom;
            return coffret;
        }).ToList();
        return principale;
    }

    /// <summary>Clé d'affectation d'un appareil : son repère calculé ; un tronçon de bornier suit son bornier.</summary>
    public static string CleAffectation(AppareilImplante a) => a.Element is null && a.Famille == "Bornier" ? a.Repere.Split('.')[0] : a.Cle;

    static ResultatArmoire Calculer(ResultatProjet r, List<AppareilImplante> elements, ConfigurationArmoire config,
        IReadOnlyList<ModeleEnveloppe>? enveloppes, IReadOnlyList<ElementMateriel> porte)
    {
        var note = new NoteDeCalcul("ARM", config.Forcages) { Groupe = "Enveloppe" };
        var reserve = Math.Clamp(config.ReservePourcent, 0, 80) / 100;
        var catalogue = enveloppes ?? CatalogueEnveloppes.Charger();
        var marque = config.Marque;
        var modeles = CatalogueEnveloppes.DeLaMarque(marque, catalogue).ToList();

        // Plus petite enveloppe du catalogue de la marque qui reçoit tout le matériel avec la réserve demandée.
        var formats = modeles.GroupBy(m => (m.Largeur, m.Hauteur, m.Type)).Select(g => g.First()).ToList();
        var candidats = formats.Where(f => !f.EstArmoire).Select(f => (M: f, N: 1))
            .Concat(formats.Where(f => f.EstArmoire).SelectMany(f => Enumerable.Range(1, CellulesMax).Select(n => (M: f, N: n))))
            .Where(c => PorteRecoit(porte, c.M.Largeur, c.M.Hauteur))
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
        var profondeurIhm = porte.Count == 0 ? 0 : porte.Max(e => e.ProfondeurMm);
        // Derrière une IHM en porte, il faut au moins la place d'un appareil modulaire sur rail.
        var profondeurMini = Math.Max(profondeurAppareils + 100, porte.Count == 0 ? 0 : profondeurIhm + ProfondeurModulaire + JeuIhm);
        var profondeurs = modeles.Where(m => Math.Abs(m.Largeur - largeur) < 1 && Math.Abs(m.Hauteur - hauteur) < 1)
            .Select(m => m.Profondeur).Distinct().OrderBy(p => p).ToArray();
        if (profondeurs.Length == 0) profondeurs = armoire ? new double[] { 400, 500, 600, 800 } : new double[] { 200, 250, 300, 400 };
        var profondeurAuto = Calibres.Superieur(profondeurs, profondeurMini) ?? profondeurs[^1];
        var profondeur = imposee
            ? config.ProfondeurImposee
            : note.Valeur(CodeProfondeur, "Profondeur", profondeurMini, profondeurAuto, "mm",
                porte.Count == 0 ? "P ≥ profondeur du plus gros appareil + 100 mm (plaque, porte, câblage)"
                    : $"P ≥ max(appareil le plus profond + 100 ; IHM en porte + {N(ProfondeurModulaire, 0)} mm d'appareil modulaire + {N(JeuIhm, 0)} mm de jeu)",
                $"appareil le plus profond : {N(profondeurAppareils)} mm" + (porte.Count == 0 ? "" : $" ; IHM en porte : {N(profondeurIhm)} mm"), RefArmoire);
        note.Verifier("Profondeur", profondeur >= profondeurMini,
            $"Profondeur {N(profondeur)} mm pour {N(profondeurMini)} mm nécessaires.", RefArmoire,
            imposee ? StatutCalcul.NonConforme : StatutCalcul.Alerte);

        var enveloppe = CatalogueEnveloppes.Trouver(marque, largeur, hauteur, profondeur, catalogue);

        // Taille imposée trop juste : on essaie par paliers, en gardant d'abord la réserve demandée et en allégeant les
        // goulottes (25 mm, puis verticales seules, puis aucune) ; ensuite seulement la réserve est réduite par pas de 5 %.
        var libre = config.ImplantationLibre;
        if (libre) gabarit = Gabarit.SansGoulotte;
        var implantation = libre ? ImplanterLibre(elements, config, largeur * cellules) : Implanter(elements, enveloppe, cellules, reserve, gabarit);
        var reserveEffective = reserve;
        var palier = 0;
        if (imposee && !implantation.Tient && !libre)
        {
            // Avec ventilation forcée, le dégagement des variateurs peut descendre au minimum saisi (notice du fabricant) :
            // essayé avant de retirer les goulottes.
            var reduit = Math.Clamp(config.DegagementVariateurVentile, 0, DegagementVariateur);
            var paliers = new[]
            {
                gabarit, gabarit with { DegagementPartage = true }, Gabarit.Fines,
                gabarit with { DegagementPartage = true, Degagement = reduit }, Gabarit.Fines with { Degagement = reduit },
                Gabarit.Verticales, Gabarit.Verticales with { Degagement = reduit },
                Gabarit.SansGoulotte, Gabarit.SansGoulotte with { Degagement = reduit },
            };
            var essais = paliers.Select((g, i) => (Palier: i, Reserve: reserve))
                .Concat(Enumerable.Range(1, (int)Math.Ceiling(reserve / 0.05))
                    .SelectMany(k => paliers.Select((g, i) => (Palier: i, Reserve: Math.Max(0, reserve - 0.05 * k)))));
            foreach (var (p, res) in essais)
            {
                var essaiPalier = p == 0 && res == reserve ? implantation : Implanter(elements, enveloppe, cellules, res, paliers[p]);
                if (!essaiPalier.Tient) continue;
                (implantation, reserveEffective, gabarit, palier) = (essaiPalier, res, paliers[p], p);
                break;
            }
        }
        var appareils = implantation.Appareils;
        AppliquerPositionsManuelles(appareils, config, largeur * cellules, hauteur);
        CalerSurRails(appareils, implantation.Bandes.Where(b => b.Rail)
            .Select(b => (b.X + gabarit.Laterale, b.X + b.Largeur - gabarit.Laterale, b.Y + b.Hauteur / 2))
            .Concat(config.Rails.Select(x => (x.X, x.X + x.Longueur, x.Y))).ToList());
        // Les rangées qui débordent de l'enveloppe ne sont pas dessinées hors de l'armoire.
        var bandesPosees = implantation.Bandes.Where(b => b.X < largeur * cellules - 1).ToList();
        // L'implantation automatique déborde, mais les appareils déplacés à la main font tout entrer : elle est retenue.
        var tient = implantation.Tient;
        var tientALaMain = !tient && appareils.Any(a => a.Manuel) && ToutSurLaPlaque(appareils, enveloppe, largeur, cellules)
            && !appareils.Where((a, i) => appareils.Skip(i + 1).Any(a.Chevauche)).Any();
        if (tientALaMain) tient = true;
        var attente = tient ? 0 : RangerEnAttente(appareils, largeur * cellules, hauteur);
        var alerte = "";
        var allegement = string.Join(" et ", new[]
        {
            gabarit.SansGoulottes ? "en retirant les goulottes (fils posés en direct)"
                : gabarit.SansHorizontales ? "en ne gardant que les goulottes verticales (25 mm)"
                : gabarit.GoulotteMax < double.MaxValue ? "en réduisant les goulottes à 25 mm"
                : palier > 0 && gabarit.DegagementPartage ? "en prenant le dégagement bas des variateurs sur la marge des presse-étoupes" : "",
            palier > 0 && gabarit.Ventile ? $"en ramenant le dégagement des variateurs à {N(gabarit.Degagement)} mm avec une ventilation forcée" : "",
        }.Where(t => t.Length > 0));
        var reduite = reserveEffective < reserve - 1e-6;
        if (libre)
            alerte = tient ? "" : "Ta disposition ne tient pas encore : " + string.Join(" ; ", new[]
            {
                appareils.Any(a => a.X >= largeur * cellules) ? "à placer : " + string.Join(", ", appareils.Where(a => a.X >= largeur * cellules).Select(a => a.Repere)) : "",
                appareils.Where(a => a.X < largeur * cellules && !ToutSurLaPlaque(new[] { a }, enveloppe, largeur, cellules)).Select(a => a.Repere).ToList() is { Count: > 0 } hors
                    ? "hors de la plaque : " + string.Join(", ", hors) : "",
                Chevauchements(appareils) is { Count: > 0 } chl ? "appareils qui se chevauchent : " + string.Join(", ", chl) : "",
            }.Where(t => t.Length > 0)) + ".";
        else if (tientALaMain)
            alerte = "L'implantation automatique ne tient pas (elle demanderait " + $"{N(auto.L)} × {N(auto.H)} mm" + (auto.N > 1 ? $" × {auto.N} cellules" : "") +
                     DegagementManquant(elements, enveloppe, config.DegagementVariateurVentile) + "), mais les appareils déplacés à la main font tout entrer : implantation manuelle retenue, sans réserve garantie.";
        else if (palier > 0 || (reduite && implantation.Tient))
            alerte = $"Le matériel ne tient dans l'armoire imposée {N(largeur)} × {N(hauteur)} mm qu'" +
                     string.Join(" et ", new[] { allegement, reduite ? $"en réduisant la place libre à {N(reserveEffective * 100)} % au lieu de {N(config.ReservePourcent)} %" : "" }
                         .Where(t => t.Length > 0)) + (palier > 0 ? " ; à valider au câblage." : ".");
        else if (!implantation.Tient && appareils.Any(a => a.Manuel))
            alerte = "Ta disposition à la main ne tient pas encore : " + string.Join(" ; ", new[]
            {
                ToutSurLaPlaque(appareils, enveloppe, largeur, cellules) ? "" :
                    "hors de la plaque : " + string.Join(", ", appareils.Where(a => !ToutSurLaPlaque(new[] { a }, enveloppe, largeur, cellules)).Select(a => a.Repere)),
                Chevauchements(appareils) is { Count: > 0 } ch ? "appareils qui se chevauchent : " + string.Join(", ", ch) : "",
            }.Where(t => t.Length > 0)) + ". Corrige-les et elle sera retenue (l'implantation automatique, elle, demanderait " +
                $"{N(auto.L)} × {N(auto.H)} mm" + (auto.N > 1 ? $" × {auto.N} cellules" : "") + ").";
        else if (!implantation.Tient)
            alerte = imposee
                ? $"Impossible de tout faire entrer dans l'armoire imposée {N(largeur)} × {N(hauteur)} mm" +
                  (cellules > 1 ? $" × {cellules} cellules" : "") + $", même sans réserve. Taille nécessaire : {N(auto.L)} × {N(auto.H)} mm" +
                  (auto.N > 1 ? $" × {auto.N} cellules" : "") + $" avec {N(config.ReservePourcent)} % de réserve."
                : "Le matériel ne tient pas dans les dimensions retenues : agrandir l'armoire ou réduire la réserve.";
        else if (profondeur < profondeurMini && imposee)
            alerte = $"Profondeur imposée {N(profondeur)} mm insuffisante : {N(profondeurMini)} mm nécessaires pour l'appareil le plus profond.";
        note.Info("Enveloppe", $"{(armoire ? "Armoire au sol" : "Coffret mural")} {marque} {enveloppe.Gamme} {N(largeur)} × {N(hauteur)} × {N(profondeur)} mm" +
            (cellules > 1 ? $", {cellules} cellules juxtaposées (largeur totale {N(largeur * cellules)} mm)" : "") +
            $" : {enveloppe.Reference} ; plaque : {enveloppe.ReferencePlaque}, surface utile {N(enveloppe.PlaqueLargeur)} × {N(enveloppe.PlaqueHauteur)} mm" +
            (enveloppe.Verifie ? "." : " (références indicatives, à vérifier)."), "Catalogues Rittal, Legrand, Schneider ; " + RefArmoire);
        var bandesLibres = libre ? RecalculerZones(bandesPosees, appareils, config.Rails, largeur * cellules, hauteur) : null;
        var placeRails = libre ? PlaceLibreRails(bandesLibres!, appareils) : null;
        note.Groupe = "Implantation";
        if (libre)
        {
            var goulottesPosees = config.Goulottes.Select(g => (g.Verticale ? g.Largeur : g.Longueur) * (g.Verticale ? g.Longueur : g.Largeur)).Sum();
            var surface = SurfaceLibre(appareils, enveloppe, largeur * cellules, cellules, goulottesPosees);
            reserveEffective = surface.Libre;
            note.Verifier("Implantation", tient,
                tient ? "Ta disposition tient : tout est sur la plaque, sans chevauchement." : alerte, RefArmoire, StatutCalcul.NonConforme);
            note.Verifier("Place libre", surface.Libre * 100 >= config.ReservePourcent - 1e-6,
                $"Plaque {N(surface.Plaque / 1e6, 3)} m² − goulottes {N(surface.Goulottes / 1e6, 3)} m² = {N(surface.Utile / 1e6, 3)} m² utiles ; " +
                $"appareils {N(surface.Appareils / 1e6, 3)} m² : {N(surface.Libre * 100)} % de place libre pour {N(config.ReservePourcent)} % demandés.",
                RefArmoire, StatutCalcul.Alerte);
            if (placeRails is { } pr)
                note.Info("Rails DIN", $"{N(pr.Longueur)} mm de rail, {N(pr.Occupe)} mm occupés : {N(pr.Libre * 100)} % de rail libre ({N(pr.Longueur - pr.Occupe)} mm).", RefArmoire);
        }
        var complet = implantation.Tient && palier == 0 && reserveEffective >= reserve - 1e-6;
        if (!libre) note.Verifier("Implantation", complet,
            complet ? $"Tout le matériel tient avec {N(config.ReservePourcent)} % de réserve ; hauteur occupée {Pct(implantation.Occupation)}." : alerte,
            RefArmoire, tient ? StatutCalcul.Alerte : StatutCalcul.NonConforme);
        note.Info("Zones CEM", "Zone propre en haut (automate, E/S, sécurité), zone intermédiaire (protections, alimentations), " +
            "zone sale en bas, au plus près des presse-étoupes des câbles moteurs : contacteurs et borniers sur rails, " +
            $"variateurs et démarreurs regroupés côte à côte sur le côté, fixés sur la plaque avec {N(DegagementVariateur, 0)} mm de dégagement " +
            "en haut et en bas pour la ventilation.", RefCem);

        var bandes = RognerSurPlaque(ZonesHorsRails(RecalculerZones(bandesPosees, appareils, config.Rails, largeur * cellules, hauteur),
            appareils, largeur * cellules, gabarit.Degagement), enveloppe, largeur, cellules);
        ControlerManuel(note, appareils, bandes, largeur * cellules, hauteur);
        var ventilee = gabarit.Ventile && !libre && !appareils.Any(a => a.Manuel);
        if (appareils.Any(a => a.Manuel) || libre)
            ventilee = ControlerDegagementVariateurs(note, appareils, enveloppe, largeur, cellules, Math.Clamp(config.DegagementVariateurVentile, 0, DegagementVariateur));

        var cablage = CalculCablage.Estimer(r, appareils, bandesPosees, largeur, enveloppe.PlaqueLargeur, enveloppe.PlaqueHauteur,
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

        var aVerifier = appareils.Where(a => a.Element is not null && !a.CotesVerifiees).ToList();
        if (aVerifier.Count > 0)
            note.Info("Cotes à vérifier", $"{aVerifier.Count} appareil(s) implanté(s) avec des cotes non relevées sur fiche : " +
                string.Join(" ; ", aVerifier.Select(a => $"{a.Repere} {a.Element!.Reference}".TrimEnd() + $" {N(a.Largeur)} × {N(a.Hauteur)} × {N(a.Profondeur)} mm ({a.Origine})")) +
                ". La référence s'affiche sous chaque appareil (en orange quand ses cotes sont estimées).", Encombrements.Source);
        var ihm = PlacerPorte(note, porte, appareils, largeur, hauteur, profondeur);

        var sources = ChampMagnetique.Sources(appareils);
        var expositions = ControlerChamps(note, appareils, sources);
        ControlerSeparation(note, bandes);

        return new ResultatArmoire
        {
            Note = note, Largeur = largeur, Hauteur = hauteur, Profondeur = profondeur, Cellules = cellules,
            Type = armoire ? "Armoire au sol" : "Coffret mural",
            Appareils = appareils, Bandes = bandes, Sources = sources, Expositions = expositions, Porte = ihm,
            LongueurRail = (bandesPosees.Where(b => b.Rail).Sum(b => b.Largeur - 2 * gabarit.Laterale) + config.Rails.Sum(x => x.Longueur)) / 1000,
            LongueurGoulotte = cablage.Goulottes.Sum(x => x.Verticale ? x.Hauteur : x.Largeur) / 1000,
            Cablage = cablage, Laterale = gabarit.Laterale,
            Marque = marque.ToString(), Gamme = enveloppe.Gamme, Reference = enveloppe.Reference, ReferencePlaque = enveloppe.ReferencePlaque,
            PlaqueLargeur = enveloppe.PlaqueLargeur, PlaqueHauteur = enveloppe.PlaqueHauteur,
            Occupation = libre ? SurfaceOccupee(appareils, enveloppe, largeur * cellules, cellules) : implantation.Occupation,
            TailleImposee = imposee, ReserveEffective = reserveEffective * 100, Libre = libre, ProfondeurNecessaire = profondeurMini,
            LargeurAttente = attente,
            Tient = tient && !(imposee && profondeur < profondeurMini), Alerte = alerte,
            VentilationForcee = ventilee, DegagementRetenu = gabarit.Degagement,
        };
    }

    /// <summary>Profondeur d'un appareil modulaire sur rail derrière une IHM en porte, et jeu de sécurité, en mm.</summary>
    public const double ProfondeurModulaire = 75, JeuIhm = 25;

    /// <summary>Cadre de porte (renforts, joint) autour des IHM et écart entre deux IHM, en mm.</summary>
    public const double CadrePorte = 80, EcartIhm = 50;

    static bool PorteRecoit(IReadOnlyList<ElementMateriel> porte, double largeur, double hauteur) =>
        porte.Count == 0 || (porte.Sum(e => e.LargeurMm) + EcartIhm * (porte.Count - 1) <= largeur - 2 * CadrePorte
                             && porte.Max(e => e.HauteurMm) <= hauteur - 2 * CadrePorte);

    /// <summary>
    /// IHM en porte : côte à côte, centrées, l'écran dans le tiers haut de la porte. Vérifie la façade et ce qui se trouve
    /// derrière sur la plaque (l'IHM dépasse à l'intérieur de toute sa profondeur).
    /// </summary>
    static List<AppareilImplante> PlacerPorte(NoteDeCalcul note, IReadOnlyList<ElementMateriel> porte, List<AppareilImplante> appareils,
        double largeur, double hauteur, double profondeur)
    {
        var liste = new List<AppareilImplante>();
        if (porte.Count == 0) return liste;
        var total = porte.Sum(e => e.LargeurMm) + EcartIhm * (porte.Count - 1);
        var x = (largeur - total) / 2;
        var haut = porte.Max(e => e.HauteurMm);
        var y = Math.Max(CadrePorte, hauteur / 3 - haut / 2);
        foreach (var e in porte)
        {
            liste.Add(new AppareilImplante
            {
                Repere = e.Repere, Designation = e.Designation, Famille = e.Famille, Zone = ZoneCem.Propre, Element = e,
                X = x, Y = y + (haut - e.HauteurMm) / 2, Largeur = e.LargeurMm, Hauteur = e.HauteurMm, Profondeur = e.ProfondeurMm,
                DissipationW = e.PertesW,
            });
            x += e.LargeurMm + EcartIhm;
        }
        note.Groupe = "Implantation";
        note.Verifier("IHM en porte", PorteRecoit(porte, largeur, hauteur),
            $"{string.Join(", ", liste.Select(a => $"{a.Repere} façade {N(a.Largeur)} × {N(a.Hauteur)} mm"))} ; " +
            $"porte {N(largeur)} × {N(hauteur)} mm, {N(CadrePorte, 0)} mm de cadre réservés tout autour.", RefArmoire, StatutCalcul.NonConforme);
        // Les appareils sont repérés dans l'armoire vue de face, comme la porte fermée.
        foreach (var ihm in liste)
        {
            var derriere = appareils.Where(a => a.X < ihm.X + ihm.Largeur && ihm.X < a.X + a.Largeur
                                                && a.Y < ihm.Y + ihm.Hauteur && ihm.Y < a.Y + a.Hauteur).ToList();
            var plusProfond = derriere.Count == 0 ? 0 : derriere.Max(a => a.Profondeur);
            var besoin = ihm.Profondeur + plusProfond + JeuIhm;
            note.Verifier($"Derrière {ihm.Repere}", besoin <= profondeur,
                derriere.Count == 0
                    ? $"Rien sur la plaque derrière {ihm.Repere} ({N(ihm.Profondeur)} mm de profondeur)."
                    : $"{N(ihm.Profondeur)} mm d'IHM + {N(plusProfond)} mm du plus profond derrière ({derriere.MaxBy(a => a.Profondeur)!.Repere}) + " +
                      $"{N(JeuIhm, 0)} mm de jeu = {N(besoin)} mm pour {N(profondeur)} mm d'armoire" +
                      (besoin <= profondeur ? "." : " : déplacer l'IHM ou ces appareils, ou prendre une armoire plus profonde."),
                RefArmoire, StatutCalcul.Alerte);
        }
        return liste;
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
                Origine = enc.Origine, CotesVerifiees = enc.CotesVerifiees,
            });
        }

        // Place réservée pour des bornes ajoutées en cours de câblage ou de mise en service.
        var puissance = BornesPuissance(r).Sum(b => b.Largeur) * (1 + ReserveBornes);
        if (puissance > 0)
            liste.Add(Bornier("X1", "Bornier de puissance", ZoneCem.Sale, puissance));
        var commande = BornesCommande(projet).Nombre * Encombrements.LargeurBorneCommande * (1 + ReserveBornes);
        if (commande > 0)
            liste.Add(Bornier("X2", "Bornier de commande", ZoneCem.Propre, commande));
        return liste;
    }

    /// <summary>Réserve de place sur les borniers (20 %).</summary>
    public const double ReserveBornes = 0.2;

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
            // Frein à alimentation séparée : deux bornes de plus vers la bobine.
            if (d.Depart is DepartMoteur { Frein: true, AlimentationFrein: AlimentationFrein.Separee230 }) actifs += 2;
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
        // Barrière : alimentation de l'émetteur et du récepteur, OSSD, EDM et réarmement ; scrutateur : alimentation, OSSD, EDM et réarmement.
        var securite = projet.Securite.Fonctions.Sum(f => Math.Max(1, f.NombreCapteurs) * f.Type switch
        {
            TypeFonctionSecurite.BarriereImmaterielle => 9,
            TypeFonctionSecurite.ScrutateurLaser => 8,
            _ => 2,
        });
        var distribution = es + securite > 0 || projet.Departs.OfType<DepartAlimentation24V>().Any() ? 10 : 0;
        // Frein 24 V DC : deux bornes de commande vers la bobine.
        var freins = 2 * projet.Departs.OfType<DepartMoteur>().Count(m => m.Frein && m.AlimentationFrein == AlimentationFrein.Continu24);
        return (es + securite + distribution + freins,
            $"{es} E/S, {securite} sécurité, {distribution} distribution 24 V" + (freins > 0 ? $", {freins} freins 24 V" : ""));
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
        var goulotteVariateurs = g.SansGoulottes ? JeuVariateurs : GoulotteVariateurs;
        var partage = g.DegagementPartage ? Math.Min(g.Degagement, MargeBasse) : 0;
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
            var appareils = elements.Where(e => e.Zone == zone && e.Famille != "Bornier").ToList();
            if (appareils.Count == 0) continue;
            if (zonePrecedente == ZoneCem.Propre && zone == ZoneCem.Sale) y += SeparationCem;
            zonePrecedente = zone;

            var variateurs = zone == ZoneCem.Sale ? appareils.Where(EstVariateur).ToList() : new List<AppareilImplante>();
            var surRail = appareils.Except(variateurs).SelectMany(e => Decouper(e, capacite)).ToList();

            // Bloc des variateurs sur le côté droit si les rails gardent au moins 40 % de la largeur ; sinon en rangées pleines en bas de zone.
            var rangeesVariateurs = Rangees(variateurs, utile, JeuVariateurs);
            var largeurBloc = rangeesVariateurs.Select(r => r.Sum(e => e.Largeur) + (r.Count - 1) * JeuVariateurs).DefaultIfEmpty(0).Max();
            var aCote = variateurs.Count > 0 && surRail.Count > 0 && utile - largeurBloc - goulotteVariateurs >= 0.4 * utile;
            var largeurRails = aCote ? utile - largeurBloc - goulotteVariateurs : utile;
            var rangeesRail = Rangees(surRail, largeurRails * (1 - reserve), JeuDeparts);
            if (rangeesRail.Any(r => r.Sum(e => e.Largeur) > largeurRails)) tient = false;

            double HauteurRail(IEnumerable<List<AppareilImplante>> r) => r.Sum(x => x.Max(e => e.Hauteur) + entreRangees);
            double HauteurBloc(IEnumerable<List<AppareilImplante>> r) => r.Sum(x => x.Max(e => e.Hauteur) + g.Degagement) + g.Degagement - entreRangees;

            if (aCote)
            {
                // Rails à gauche et bloc des variateurs à droite partagent la même hauteur de zone.
                var hBloc = HauteurBloc(rangeesVariateurs);
                var h = Math.Max(HauteurRail(rangeesRail), hBloc - partage);
                Reserver(h - entreRangees);
                var x0 = cellule * largeur + dx + margeLaterale;
                var yRail = y;
                foreach (var rangee in rangeesRail)
                {
                    var hr = Poser(rangee, x0, yRail, JeuDeparts, true);
                    bandes.Add(new BandeZone(zone, cellule * largeur + dx, yRail - entreRangees / 2, margeLaterale + largeurRails + goulotteVariateurs / 2, hr + entreRangees));
                    yRail += hr + entreRangees;
                }
                var xBloc = x0 + largeurRails + goulotteVariateurs;
                var yBloc = y - entreRangees / 2 + g.Degagement;
                foreach (var rangee in rangeesVariateurs)
                    yBloc += Poser(rangee, xBloc + largeurBloc - (rangee.Sum(e => e.Largeur) + (rangee.Count - 1) * JeuVariateurs), yBloc, JeuVariateurs, false)
                             + g.Degagement;
                bandes.Add(new BandeZone(zone, xBloc - goulotteVariateurs / 2, y - entreRangees / 2,
                    cellule * largeur + dx + plaque - (xBloc - goulotteVariateurs / 2), Math.Max(h, hBloc), false));
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
                var hr = rangee.Max(e => e.Hauteur) + 2 * g.Degagement - entreRangees;
                Reserver(rangee == rangeesVariateurs[^1] ? hr - partage : hr);
                var yr = y - entreRangees / 2 + g.Degagement;
                Poser(rangee, cellule * largeur + dx + margeLaterale, yr, JeuVariateurs, false);
                bandes.Add(new BandeZone(zone, cellule * largeur + dx, y - entreRangees / 2, plaque, hr + entreRangees, false));
                y += hr + entreRangees;
            }
        }
        // Zone des borniers en bas de la plaque, au plus près des presse-étoupes : commande (propre) à gauche, puissance (sale)
        // à droite, séparées pour la CEM ; la place libre reste au-dessus, entre le matériel et les borniers.
        var occupeAvant = cellule * (bas - haut) + Math.Max(0, y - entreRangees - haut);
        var borniers = elements.Where(e => e.Famille == "Bornier" && e.Zone != ZoneCem.HorsArmoire)
            .OrderBy(e => e.Zone == ZoneCem.Propre ? 0 : e.Zone == ZoneCem.Intermediaire ? 1 : 2).SelectMany(e => Decouper(e, capacite)).ToList();
        var rangeesBornier = RangeesBorniers(borniers, utile * (1 - reserve));
        if (rangeesBornier.Count > 0)
        {
            // Les borniers descendent dans la marge basse : il n'y reste que la goulotte d'arrivée des câbles.
            var hTotal = rangeesBornier.Sum(r => r.Max(e => e.Hauteur) + entreRangees) - entreRangees - (MargeBasse - MargeSousBorniers);
            Reserver(hTotal);
            if (y + hTotal <= bas) y = Math.Max(y, bas - hTotal);
            foreach (var rangee in rangeesBornier)
            {
                var hr = rangee.Max(e => e.Hauteur);
                var x0 = cellule * largeur + dx + margeLaterale;
                var x = x0;
                for (var i = 0; i < rangee.Count; i++)
                {
                    if (i > 0) x += rangee[i].Zone != rangee[i - 1].Zone ? SeparationBorniers : JeuDeparts;
                    places.Add(Copier(rangee[i], x, y));
                    x += rangee[i].Largeur;
                }
                bandes.Add(new BandeZone(rangee.Any(e => e.Zone == ZoneCem.Sale) ? ZoneCem.Sale : rangee[0].Zone, cellule * largeur + dx, y - entreRangees / 2, plaque, hr + entreRangees, Bornier: true));
                y += hr + entreRangees;
            }
            occupeAvant += hTotal + entreRangees;
        }
        if (cellule >= cellules) tient = false;
        var disponible = cellules * (bas - haut);
        return new Implantation(tient, places, bandes, disponible > 0 ? Math.Min(100, 100 * occupeAvant / disponible) : 100);
    }

    /// <summary>Écart entre le bornier de commande et celui de puissance posés sur le même rail (séparation CEM), en mm.</summary>
    public const double SeparationBorniers = 50;

    /// <summary>Place laissée sous les borniers pour la goulotte d'arrivée des câbles, en mm.</summary>
    public const double MargeSousBorniers = 60;

    /// <summary>Rangées de borniers : tronçons à la suite, un écart de séparation entre commande et puissance.</summary>
    static List<List<AppareilImplante>> RangeesBorniers(List<AppareilImplante> borniers, double capacite)
    {
        var rangees = new List<List<AppareilImplante>>();
        var courante = new List<AppareilImplante>();
        double occupe = 0;
        foreach (var b in borniers)
        {
            var ecart = courante.Count == 0 ? 0 : courante[^1].Zone != b.Zone ? SeparationBorniers : JeuDeparts;
            if (courante.Count > 0 && occupe + ecart + b.Largeur > capacite)
            {
                rangees.Add(courante);
                courante = new List<AppareilImplante>();
                occupe = 0;
                ecart = 0;
            }
            courante.Add(b);
            occupe += ecart + b.Largeur;
        }
        if (courante.Count > 0) rangees.Add(courante);
        return rangees;
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
        X = x, Y = y, Largeur = e.Largeur, Hauteur = e.Hauteur, Profondeur = e.Profondeur, DissipationW = e.DissipationW, Origine = e.Origine, CotesVerifiees = e.CotesVerifiees,
    };

    /// <summary>Écart entre l'armoire et la zone « à placer », en mm.</summary>
    public const double EcartAttente = 60;

    /// <summary>
    /// Range en colonnes, juste à droite de l'armoire, les appareils que l'implantation automatique n'a pas pu y mettre,
    /// pour qu'on les voie et qu'on puisse les glisser dans l'armoire. Renvoie la largeur occupée.
    /// </summary>
    static double RangerEnAttente(List<AppareilImplante> appareils, double largeurTotale, double hauteur)
    {
        var dehors = appareils.Where(a => !a.Manuel && a.X + a.Largeur > largeurTotale + 0.5).ToList();
        if (dehors.Count == 0) return 0;
        double x = largeurTotale + EcartAttente, y = 40, colonne = 0;
        foreach (var a in dehors)
        {
            if (y > 40 && y + a.Hauteur > hauteur - 20)
            {
                x += colonne + 20;
                y = 40;
                colonne = 0;
            }
            (a.X, a.Y) = (x, y);
            y += a.Hauteur + 20;
            colonne = Math.Max(colonne, a.Largeur);
        }
        return x + colonne + 20 - largeurTotale;
    }

    /// <summary>
    /// Raison la plus fréquente d'un refus de l'implantation automatique : un variateur plus haut que la plaque une fois
    /// ajoutés ses dégagements de ventilation haut et bas.
    /// </summary>
    static string DegagementManquant(IEnumerable<AppareilImplante> elements, ModeleEnveloppe enveloppe, double reduit)
    {
        var v = elements.Where(EstVariateur).OrderByDescending(e => e.Hauteur).FirstOrDefault();
        if (v is null || v.Hauteur + 2 * DegagementVariateur <= enveloppe.PlaqueHauteur) return "";
        return $" : le variateur {v.Repere} fait {N(v.Hauteur)} mm de haut et demande {N(DegagementVariateur)} mm libres au-dessus et au-dessous " +
               $"pour sa ventilation, soit {N(v.Hauteur + 2 * DegagementVariateur)} mm pour {N(enveloppe.PlaqueHauteur)} mm de plaque" +
               (v.Hauteur + 2 * reduit > enveloppe.PlaqueHauteur
                   ? $", et même {N(v.Hauteur + 2 * reduit)} mm avec le dégagement réduit à {N(reduit)} mm sous ventilation forcée" : "");
    }

    /// <summary>
    /// Appareils posés sur la plaque hors de toute rangée (variateurs fixés directement, disposition faite à la main) :
    /// chacun reçoit sa propre zone, avec le dégagement de ventilation d'un variateur, pour que les zones restent visibles.
    /// </summary>
    static List<BandeZone> ZonesHorsRails(List<BandeZone> bandes, IReadOnlyList<AppareilImplante> appareils, double largeurTotale, double degagement)
    {
        var resultat = new List<BandeZone>(bandes);
        foreach (var a in appareils.Where(a => a.X < largeurTotale && a.Zone != ZoneCem.HorsArmoire && a.Famille != "Bornier"))
        {
            if (resultat.Any(b => a.CentreX >= b.X && a.CentreX <= b.X + b.Largeur && a.CentreY >= b.Y && a.CentreY <= b.Y + b.Hauteur)) continue;
            var marge = EstVariateur(a) ? degagement : 15;
            resultat.Add(new BandeZone(a.Zone, a.X - 15, a.Y - marge, a.Largeur + 30, a.Hauteur + 2 * marge, false));
        }
        return resultat;
    }

    /// <summary>Limite les zones dessinées à la plaque de montage de leur cellule : une zone ne sort jamais de l'enveloppe.</summary>
    static List<BandeZone> RognerSurPlaque(IEnumerable<BandeZone> bandes, ModeleEnveloppe enveloppe, double largeur, int cellules)
    {
        var dx = (largeur - enveloppe.PlaqueLargeur) / 2;
        var dy = (enveloppe.Hauteur - enveloppe.PlaqueHauteur) / 2;
        var resultat = new List<BandeZone>();
        foreach (var b in bandes)
        {
            var c = Math.Clamp((int)Math.Floor((b.X + b.Largeur / 2) / largeur), 0, Math.Max(0, cellules - 1));
            var x0 = Math.Max(b.X, c * largeur + dx);
            var x1 = Math.Min(b.X + b.Largeur, c * largeur + dx + enveloppe.PlaqueLargeur);
            var y0 = Math.Max(b.Y, dy);
            var y1 = Math.Min(b.Y + b.Hauteur, dy + enveloppe.PlaqueHauteur);
            if (x1 - x0 >= 1 && y1 - y0 >= 1)
                resultat.Add(b with { X = x0, Y = y0, Largeur = x1 - x0, Hauteur = y1 - y0 });
        }
        return resultat;
    }

    /// <summary>
    /// Contrôle, sur une disposition faite à la main, la place libre au-dessus et au-dessous de chaque variateur ou
    /// démarreur : bord de plaque ou appareil le plus proche dans sa largeur, comparé au dégagement de ventilation.
    /// </summary>
    static bool ControlerDegagementVariateurs(NoteDeCalcul note, List<AppareilImplante> appareils, ModeleEnveloppe enveloppe, double largeur,
        int cellules, double reduit)
    {
        var dy = (enveloppe.Hauteur - enveloppe.PlaqueHauteur) / 2;
        var variateurs = appareils.Where(a => EstVariateur(a) && a.X < largeur * cellules).ToList();
        if (variateurs.Count == 0) return false;
        var manques = new List<string>();
        var sousMinimum = false;
        foreach (var v in variateurs)
        {
            var voisins = appareils.Where(a => !ReferenceEquals(a, v) && a.X < largeur * cellules
                                               && a.X < v.X + v.Largeur && a.X + a.Largeur > v.X).ToList();
            var dessus = v.Y - voisins.Where(a => a.Y + a.Hauteur <= v.Y + PasManuel).Select(a => a.Y + a.Hauteur).Append(dy).Max();
            var dessous = voisins.Where(a => a.Y >= v.Y + v.Hauteur - PasManuel).Select(a => a.Y).Append(dy + enveloppe.PlaqueHauteur).Min() - (v.Y + v.Hauteur);
            if (dessus < DegagementVariateur - 1e-6 || dessous < DegagementVariateur - 1e-6)
                manques.Add($"{v.Repere} : {N(Math.Max(0, dessus))} mm au-dessus et {N(Math.Max(0, dessous))} mm au-dessous");
            if (dessus < reduit - 1e-6 || dessous < reduit - 1e-6) sousMinimum = true;
        }
        note.Groupe = "Implantation";
        if (manques.Count == 0)
            note.Verifier("Dégagement des variateurs", true,
                $"Chaque variateur garde au moins {N(DegagementVariateur)} mm libres au-dessus et au-dessous pour sa ventilation.", RefCem);
        else if (!sousMinimum)
            note.Verifier("Dégagement des variateurs", false,
                $"Moins de {N(DegagementVariateur)} mm au-dessus ou au-dessous ({string.Join(" ; ", manques)}), mais au moins {N(reduit)} mm : " +
                "ventilation forcée ajoutée à l'armoire, voir le bilan thermique. Minimum à confirmer dans la notice du variateur.",
                RefCem, StatutCalcul.Alerte);
        else
            note.Verifier("Dégagement des variateurs", false,
                $"Ventilation du variateur impossible : {string.Join(" ; ", manques)}, sous le minimum de {N(reduit)} mm même avec une ventilation " +
                "forcée. Le variateur chaufferait ou se déclasserait : armoire trop petite ou variateur à déplacer.", RefCem, StatutCalcul.NonConforme);
        return manques.Count > 0 && !sousMinimum;
    }

    /// <summary>Vrai si chaque appareil est entièrement sur la plaque de montage d'une des cellules.</summary>
    static bool ToutSurLaPlaque(IEnumerable<AppareilImplante> appareils, ModeleEnveloppe enveloppe, double largeur, int cellules)
    {
        var dx = (largeur - enveloppe.PlaqueLargeur) / 2;
        var dy = (enveloppe.Hauteur - enveloppe.PlaqueHauteur) / 2;
        return appareils.All(a =>
        {
            var c = (int)Math.Floor(a.X / largeur);
            var x0 = c * largeur + dx;
            return c >= 0 && c < cellules && a.X >= x0 - PasManuel && a.X + a.Largeur <= x0 + enveloppe.PlaqueLargeur + PasManuel
                   && a.Y >= dy - PasManuel && a.Y + a.Hauteur <= dy + enveloppe.PlaqueHauteur + PasManuel;
        });
    }

    /// <summary>
    /// Implantation libre : chaque appareil reste là où l'utilisateur l'a posé ; un appareil sans position (ajouté depuis)
    /// part dans la zone « à placer ». Il n'y a ni rangée ni goulotte automatique : seulement les rails et goulottes de l'utilisateur.
    /// </summary>
    static Implantation ImplanterLibre(IReadOnlyList<AppareilImplante> elements, ConfigurationArmoire config, double largeurTotale)
    {
        // Un bornier coupé en tronçons dans la disposition de base garde ses tronçons.
        IEnumerable<AppareilImplante> Troncons(AppareilImplante e)
        {
            var n = 0;
            while (e.Famille == "Bornier" && !config.Positions.ContainsKey(e.Cle) && config.Positions.ContainsKey($"{e.Repere}.{n + 1}")) n++;
            return n > 1 ? Decouper(e, e.Largeur / n + 0.01) : new[] { e };
        }
        var appareils = elements.SelectMany(Troncons).Select(e => new AppareilImplante
        {
            Repere = e.Repere, Designation = e.Designation, Famille = e.Famille, Zone = e.Zone, Element = e.Element,
            Largeur = e.Largeur, Hauteur = e.Hauteur, Profondeur = e.Profondeur, DissipationW = e.DissipationW, Origine = e.Origine, CotesVerifiees = e.CotesVerifiees,
            X = largeurTotale + EcartAttente, Y = 0,
        }).ToList();
        var tous = appareils.All(a => config.Positions.ContainsKey(a.Cle));
        return new Implantation(tous, appareils, new List<BandeZone>(), 0);
    }

    /// <summary>Part de la plaque couverte par les appareils posés dessus, en %.</summary>
    static double SurfaceOccupee(IEnumerable<AppareilImplante> appareils, ModeleEnveloppe enveloppe, double largeurTotale, int cellules) =>
        enveloppe.PlaqueLargeur * enveloppe.PlaqueHauteur <= 0 ? 0
            : 100 * appareils.Where(a => a.X < largeurTotale).Sum(a => a.Largeur * a.Hauteur) / (enveloppe.PlaqueLargeur * enveloppe.PlaqueHauteur * Math.Max(1, cellules));

    /// <summary>Surfaces en mm² : plaque de fond complète, goulottes posées, reste utile, appareils, et part libre de l'utile (0 à 1).</summary>
    public record Surfaces(double Plaque, double Goulottes, double Utile, double Appareils, double Libre);

    /// <summary>
    /// Place libre : la plaque de fond entière est l'espace de départ ; on retire la surface des goulottes, puis celle des
    /// appareils posés. La place libre est rapportée à la surface utile (plaque moins goulottes).
    /// </summary>
    public static Surfaces SurfaceLibre(IEnumerable<AppareilImplante> appareils, ModeleEnveloppe enveloppe, double largeurTotale, int cellules,
        double surfaceGoulottes)
    {
        var plaque = enveloppe.PlaqueLargeur * enveloppe.PlaqueHauteur * Math.Max(1, cellules);
        var utile = Math.Max(0, plaque - surfaceGoulottes);
        var occupe = appareils.Where(a => a.X < largeurTotale).Sum(a => a.Largeur * a.Hauteur);
        return new Surfaces(plaque, surfaceGoulottes, utile, occupe, utile <= 0 ? 0 : Math.Max(0, 1 - occupe / utile));
    }

    /// <summary>Longueur de rail DIN posée, largeur occupée par les appareils qui y sont fixés et part libre (0 à 1), ou null sans rail.</summary>
    public record PlaceRails(double Longueur, double Occupe, double Libre);

    static PlaceRails? PlaceLibreRails(IReadOnlyList<BandeZone> bandes, IReadOnlyList<AppareilImplante> appareils)
    {
        var rails = bandes.Where(b => b.Rail).ToList();
        var longueur = rails.Sum(b => b.Largeur);
        if (longueur <= 0) return null;
        var occupe = Math.Min(longueur, rails.Sum(b => Dans(b, appareils).Where(a => !EstVariateur(a)).Sum(a => a.Largeur)));
        return new PlaceRails(longueur, occupe, 1 - occupe / longueur);
    }

    /// <summary>
    /// Passe en implantation libre : la disposition calculée (appareils, rails, goulottes) devient celle de l'utilisateur,
    /// qui peut tout déplacer, allonger, changer ou supprimer. L'enveloppe est figée à sa taille actuelle.
    /// </summary>
    public static void Figer(ResultatArmoire a, ConfigurationArmoire config)
    {
        foreach (var x in a.Appareils.Where(x => x.X < a.LargeurTotale))
            config.Positions[x.Cle] = new PositionManuelle { X = x.X, Y = x.Y };
        foreach (var b in a.Bandes.Where(b => b.Rail && b.Id is null))
            config.Rails.Add(new RailManuel { X = b.X + a.Laterale, Y = b.Y + b.Hauteur / 2, Longueur = Math.Max(50, b.Largeur - 2 * a.Laterale) });
        foreach (var g in a.Cablage.Goulottes.Where(g => !g.Manuelle))
            config.Goulottes.Add(new GoulotteManuelle
            {
                X = g.X, Y = g.Y, Verticale = g.Verticale, Longueur = g.Verticale ? g.Hauteur : g.Largeur,
                Largeur = g.Modele.Largeur, Hauteur = g.Modele.Hauteur,
            });
        var avant = config.TailleImposee;
        config.TailleImposee = true;
        config.LargeurImposee = a.Largeur;
        config.HauteurImposee = a.Hauteur;
        config.ProfondeurImposee = a.Profondeur;
        config.CellulesImposees = a.Cellules;
        if (!config.ImplantationLibre) config.TailleImposeeAvantLibre = avant;
        config.ImplantationLibre = true;
    }

    /// <summary>
    /// Un appareil modulaire posé à la main se cale en hauteur sur le rail DIN qu'il touche (axe du rail au milieu de
    /// l'appareil, le plus proche s'il en touche deux) ; il reste libre le long du rail. Les variateurs, fixés sur la plaque,
    /// et les appareils posés hors de tout rail ne bougent pas.
    /// </summary>
    static void CalerSurRails(List<AppareilImplante> appareils, IReadOnlyList<(double X0, double X1, double Axe)> rails)
    {
        foreach (var a in appareils.Where(a => a.Manuel && !EstVariateur(a)))
        {
            var rail = rails.Where(r => a.CentreX >= r.X0 && a.CentreX <= r.X1 && r.Axe >= a.Y && r.Axe <= a.Y + a.Hauteur)
                .OrderBy(r => Math.Abs(r.Axe - a.CentreY)).Select(r => (double?)r.Axe).FirstOrDefault();
            if (rail is { } axe) a.Y = axe - a.Hauteur / 2;
        }
    }

    static void AppliquerPositionsManuelles(List<AppareilImplante> appareils, ConfigurationArmoire config, double largeur, double hauteur)
    {
        foreach (var a in appareils)
        {
            if (!config.Positions.TryGetValue(a.Cle, out var p)) continue;
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

    static List<string> Chevauchements(List<AppareilImplante> appareils)
    {
        var liste = new List<string>();
        for (var i = 0; i < appareils.Count; i++)
            for (var j = i + 1; j < appareils.Count; j++)
                if (appareils[i].Chevauche(appareils[j]))
                    liste.Add($"{appareils[i].Repere} / {appareils[j].Repere}");
        return liste;
    }

    static void ControlerManuel(NoteDeCalcul note, List<AppareilImplante> appareils, List<BandeZone> bandes, double largeur, double hauteur)
    {
        note.Groupe = "Implantation";
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

        var chevauchements = Chevauchements(appareils);
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
        note.Groupe = "Compatibilité électromagnétique";
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
        note.Groupe = "Compatibilité électromagnétique";
        // La zone des borniers mêle commande et puissance par nature : ses deux borniers sont séparés sur le rail.
        var propres = bandes.Where(b => b.Zone == ZoneCem.Propre && !b.Bornier).ToList();
        var sales = bandes.Where(b => b.Zone == ZoneCem.Sale && !b.Bornier).ToList();
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
