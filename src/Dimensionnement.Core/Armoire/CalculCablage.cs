using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Armoire;

/// <summary>Fils d'une section : nombre et usage (puissance, commande, terre).</summary>
public record FilsSection(double Section, int Nombre, string Usage);

/// <summary>Modèle de goulotte : largeur sur la plaque et hauteur, en mm.</summary>
public record ModeleGoulotte(double Largeur, double Hauteur)
{
    public string Libelle => $"{Largeur:0} × {Hauteur:0} mm";
    public double Section => Largeur * Hauteur;
}

/// <summary>Une goulotte posée sur la plaque ; Remplissage en % de sa section.</summary>
public record GoulottePlacee(double X, double Y, double Largeur, double Hauteur, bool Verticale, ModeleGoulotte Modele,
    int Fils, double Remplissage, Guid? Id = null)
{
    /// <summary>Ajoutée par l'utilisateur.</summary>
    public bool Manuelle => Id is not null;
    public double Longueur => Verticale ? Hauteur : Largeur;
}

/// <summary>Largeur réservée aux goulottes : verticales sur les côtés, horizontales entre deux rangées, en mm.</summary>
public record Gabarit(double Laterale, double EntreRangees)
{
    public static readonly Gabarit Defaut = new(CalculArmoire.MargeLaterale, CalculArmoire.EntreRangees);
}

public class ResultatCablage
{
    public IReadOnlyList<FilsSection> Fils { get; init; } = Array.Empty<FilsSection>();
    public IReadOnlyList<GoulottePlacee> Goulottes { get; init; } = Array.Empty<GoulottePlacee>();
    public ModeleGoulotte Horizontale { get; init; } = CalculCablage.Modeles[3];
    public ModeleGoulotte Verticale { get; init; } = CalculCablage.Modeles[3];
    public Gabarit Gabarit { get; init; } = Gabarit.Defaut;

    /// <summary>Longueur moyenne d'un fil, en m.</summary>
    public double LongueurMoyenne { get; init; }
    public int NombreFils => Fils.Sum(f => f.Nombre);
}

/// <summary>
/// Estimation du filerie de l'armoire à partir des appareils implantés et des sections de leurs départs, puis choix
/// et implantation des goulottes : une horizontale sous chaque rangée de rail, une verticale de chaque côté de la plaque
/// et une entre les rails et le bloc des variateurs. Taux de remplissage visé ≤ 50 % (règle de l'art, réserve d'évolution).
/// </summary>
public static class CalculCablage
{
    public const string Reference = "Règle de l'art : remplissage des goulottes ≤ 50 % ; NF EN 60204-1, 13.5 (goulottes) ; diamètres H07V-K";
    public const double RemplissageMax = 50;

    /// <summary>Section des fils de commande et d'E/S, en mm².</summary>
    public const double SectionCommande = 0.75;

    /// <summary>Goulottes à fentes standard (largeur × hauteur), de la plus petite à la plus grande.</summary>
    public static readonly ModeleGoulotte[] Modeles =
    {
        new(25, 40), new(25, 60), new(40, 40), new(40, 60), new(40, 80), new(60, 60), new(60, 80),
        new(80, 80), new(80, 100), new(100, 100), new(120, 100),
    };

    /// <summary>Diamètre extérieur d'un fil souple H07V-K (H05V-K sous 1 mm²), en mm.</summary>
    public static double Diametre(double section) => section switch
    {
        <= 0.5 => 2.1,
        <= 0.75 => 2.4,
        <= 1 => 2.6,
        <= 1.5 => 3.0,
        <= 2.5 => 3.6,
        <= 4 => 4.2,
        <= 6 => 4.8,
        <= 10 => 6.1,
        <= 16 => 7.2,
        <= 25 => 8.9,
        <= 35 => 10.0,
        <= 50 => 12.0,
        <= 70 => 14.0,
        <= 95 => 16.0,
        _ => 19.0,
    };

    public static double Encombrement(IEnumerable<FilsSection> fils) => fils.Sum(f => f.Nombre * Math.PI * Math.Pow(Diametre(f.Section), 2) / 4);

    /// <summary>Plus petite goulotte dont le remplissage reste sous le maximum, ou la plus grande.</summary>
    public static ModeleGoulotte Choisir(double surface, double largeurMax = double.MaxValue) =>
        Modeles.Where(m => m.Largeur <= largeurMax).FirstOrDefault(m => 100 * surface / m.Section <= RemplissageMax)
        ?? Modeles.Where(m => m.Largeur <= largeurMax).DefaultIfEmpty(Modeles[^1]).Last();

    /// <summary>Fils partant de chaque appareil implanté (comptés une fois, côté départ du fil).</summary>
    public static List<FilsSection> Fils(AppareilImplante a, ResultatProjet r)
    {
        var fils = new List<FilsSection>();
        var e = a.Element;
        if (e is null) return fils;
        var depart = e.DepartId is { } id ? r.Departs.FirstOrDefault(d => d.Depart.Id == id) : null;
        var section = depart?.Section ?? r.SectionArrivee;
        var conducteurs = (depart?.Depart.Alimentation ?? r.Projet.Reseau.Alimentation) switch
        {
            TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => 2, TypeAlimentation.Tri400 => 3, _ => 4,
        };
        var poles = e.Poles > 0 ? e.Poles : conducteurs;
        void Ajouter(double s, int n, string usage) { if (n > 0) fils.Add(new FilsSection(s, n, usage)); }

        // Tête de départ : alimentation depuis le répartiteur et conducteur de protection.
        if (depart is not null && ReferenceEquals(depart.Materiel, e))
        {
            Ajouter(section, conducteurs, "Puissance");
            Ajouter(section, 1, "Terre");
        }
        switch (e.Famille)
        {
            case "Interrupteur-sectionneur" or "Disjoncteur" or "Sectionneur porte-fusibles" or "Relais thermique":
                Ajouter(section, poles, "Puissance");
                if (e.Famille == "Relais thermique") Ajouter(SectionCommande, 2, "Commande");
                break;
            case "Disjoncteur moteur":
                Ajouter(section, poles, "Puissance");
                Ajouter(SectionCommande, 2, "Commande");
                break;
            case "Contacteur":
                Ajouter(section, poles, "Puissance");
                Ajouter(SectionCommande, 4, "Commande");
                break;
            case "Variateur":
                Ajouter(section, poles, "Puissance");
                Ajouter(section, 1, "Terre");
                Ajouter(SectionCommande, 6, "Commande");
                break;
            case "Démarreur progressif":
                Ajouter(section, poles, "Puissance");
                Ajouter(SectionCommande, 4, "Commande");
                break;
            case "Alimentation":
                Ajouter(1.5, 2, "Puissance");
                Ajouter(2.5, 2, "Commande");
                break;
            case "Automate":
                Ajouter(1, 2, "Commande");
                Ajouter(SectionCommande, VoiesIntegrees(r), "Commande");
                break;
            case "Entrées/sorties":
                Ajouter(1, 2, "Commande");
                Ajouter(SectionCommande, VoiesCarte(e.Calibre, r), "Commande");
                break;
            case "Sécurité":
                var capacite = r.Securite?.Capacites.FirstOrDefault(c => c.Repere == e.RepereOrigine);
                Ajouter(1, 2, "Commande");
                Ajouter(SectionCommande, capacite is null ? 4 : 2 * capacite.EntreesUtilisees + 2 * capacite.SortiesUtilisees, "Commande");
                break;
            case "Fusible" or "Bornier":
                break;
            default:
                Ajouter(1.5, 2, "Commande");
                break;
        }
        return fils;
    }

    /// <summary>Voies câblées sur les E/S intégrées à la CPU (les voies de réserve ne sont pas câblées).</summary>
    static int VoiesIntegrees(ResultatProjet r) =>
        r.Automate?.Plan?.Besoins.Sum(b => (int)Math.Ceiling(b.Integrees * Taux(b.Type, r)) * (b.Type[0] == 'A' ? 2 : 1)) ?? 0;

    /// <summary>Fils d'une carte d'E/S : voies câblées selon le taux d'utilisation du type, 2 fils par voie analogique, plus les communs.</summary>
    static int VoiesCarte(string calibre, ResultatProjet r)
    {
        var morceaux = calibre.Split(' ');
        if (morceaux.Length < 3 || !int.TryParse(morceaux[0], out var voies)) return 0;
        var type = morceaux[^1];
        return (int)Math.Ceiling(voies * Taux(type, r)) * (type.StartsWith('A') ? 2 : 1) + 2;
    }

    /// <summary>Voies saisies / voies disponibles d'un type, CPU et stations comprises.</summary>
    static double Taux(string type, ResultatProjet r)
    {
        var besoins = (r.Automate?.Plan?.Besoins ?? new()).Concat(r.Automate?.Stations.SelectMany(s => s.Besoins) ?? Enumerable.Empty<BesoinVoies>())
            .Where(b => b.Type == type).ToList();
        var dispo = besoins.Sum(b => b.Disponibles);
        return dispo > 0 ? Math.Min(1, besoins.Sum(b => b.Saisies) / (double)dispo) : 1;
    }

    static List<FilsSection> Regrouper(IEnumerable<FilsSection> fils) => fils
        .GroupBy(f => (f.Section, f.Usage)).Select(g => new FilsSection(g.Key.Section, g.Sum(f => f.Nombre), g.Key.Usage))
        .OrderByDescending(f => f.Section).ToList();

    /// <summary>
    /// Fils par appareil, goulottes nécessaires et gabarit à réserver pour elles. Les fils d'une rangée passent dans la
    /// goulotte horizontale qui la longe, puis descendent par les deux goulottes verticales jusqu'aux borniers.
    /// </summary>
    public static ResultatCablage Estimer(ResultatProjet r, IReadOnlyList<AppareilImplante> appareils, IReadOnlyList<BandeZone> bandes,
        double largeurCellule, double plaqueLargeur, double plaqueHauteur, double enveloppeHauteur, int cellules, Gabarit gabarit)
    {
        var parAppareil = appareils.ToDictionary(a => a, a => Fils(a, r));
        var fils = Regrouper(parAppareil.Values.SelectMany(f => f));

        // Charge des goulottes horizontales : la rangée la plus chargée ; verticales : la moitié des fils de la cellule la plus chargée.
        var rails = bandes.Where(b => b.Rail).ToList();
        var chargeRangees = rails.Select(b => Encombrement(CalculArmoire.Dans(b, appareils).SelectMany(a => parAppareil[a]))).ToList();
        var horizontale = Choisir(chargeRangees.DefaultIfEmpty(0).Max());
        var dx = (largeurCellule - plaqueLargeur) / 2;
        var chargeCellules = Enumerable.Range(0, Math.Max(1, cellules))
            .Select(c => Encombrement(appareils.Where(a => (int)(a.CentreX / largeurCellule) == c).SelectMany(a => parAppareil[a])) / 2).ToList();
        var verticale = Choisir(chargeCellules.DefaultIfEmpty(0).Max());
        var nouveau = new Gabarit(Math.Max(CalculArmoire.MargeLaterale, verticale.Largeur + 10),
            Math.Max(CalculArmoire.EntreRangees, horizontale.Largeur + 20));

        // Implantation : sous chaque rangée de rail, au-dessus de la première rangée de chaque cellule, et sur les côtés.
        var goulottes = new List<GoulottePlacee>();
        var haut = (enveloppeHauteur - plaqueHauteur) / 2;
        for (var i = 0; i < rails.Count; i++)
        {
            var b = rails[i];
            var x = b.X + gabarit.Laterale;
            var cellule = (int)(b.X / largeurCellule);
            var finCellule = cellule * largeurCellule + dx + plaqueLargeur - gabarit.Laterale;
            var largeur = Math.Min(b.X + b.Largeur, finCellule) - x;
            if (b.Largeur < plaqueLargeur - 1) largeur = b.Largeur - gabarit.Laterale - CalculArmoire.GoulotteVariateurs / 2;
            var remplissage = 100 * chargeRangees[i] / horizontale.Section;
            var nb = CalculArmoire.Dans(b, appareils).Sum(a => parAppareil[a].Sum(f => f.Nombre));
            goulottes.Add(new GoulottePlacee(x, b.Y + b.Hauteur - horizontale.Largeur / 2, largeur, horizontale.Largeur, false, horizontale, nb, remplissage));
            var premiere = !rails.Take(i).Any(p => (int)(p.X / largeurCellule) == cellule);
            if (premiere)
                goulottes.Add(new GoulottePlacee(x, b.Y - horizontale.Largeur / 2, largeur, horizontale.Largeur, false, horizontale, nb, remplissage));
        }
        for (var c = 0; c < Math.Max(1, cellules); c++)
        {
            var gauche = c * largeurCellule + dx + (gabarit.Laterale - verticale.Largeur) / 2;
            var droite = c * largeurCellule + dx + plaqueLargeur - gabarit.Laterale + (gabarit.Laterale - verticale.Largeur) / 2;
            var nb = appareils.Where(a => (int)(a.CentreX / largeurCellule) == c).Sum(a => parAppareil[a].Sum(f => f.Nombre)) / 2;
            var remplissage = 100 * chargeCellules[c] / verticale.Section;
            goulottes.Add(new GoulottePlacee(gauche, haut + 10, verticale.Largeur, plaqueHauteur - 20, true, verticale, nb, remplissage));
            goulottes.Add(new GoulottePlacee(droite, haut + 10, verticale.Largeur, plaqueHauteur - 20, true, verticale, nb, remplissage));
        }
        foreach (var b in bandes.Where(b => !b.Rail && b.Largeur < plaqueLargeur - 1))
        {
            // Goulotte entre les rails et le bloc des variateurs : fils des variateurs vers leurs borniers.
            var charge = Encombrement(CalculArmoire.Dans(b, appareils).SelectMany(a => parAppareil[a]));
            var modele = Choisir(charge, CalculArmoire.GoulotteVariateurs - 10);
            goulottes.Add(new GoulottePlacee(b.X - modele.Largeur / 2, b.Y, modele.Largeur, b.Hauteur, true, modele,
                CalculArmoire.Dans(b, appareils).Sum(a => parAppareil[a].Sum(f => f.Nombre)), 100 * charge / modele.Section));
        }

        return new ResultatCablage
        {
            Fils = fils, Goulottes = goulottes, Horizontale = horizontale, Verticale = verticale, Gabarit = nouveau,
            LongueurMoyenne = (plaqueLargeur / 2 + plaqueHauteur / 2) / 1000 + 0.3,
        };
    }

    public static void Noter(NoteDeCalcul note, ResultatCablage c, int appareils)
    {
        if (c.NombreFils == 0) return;
        note.Info("Estimation du câblage",
            $"{c.NombreFils} fils pour {appareils} appareils implantés : " +
            string.Join(", ", c.Fils.GroupBy(f => f.Section).OrderByDescending(g => g.Key)
                .Select(g => $"{g.Sum(f => f.Nombre)} × {N(g.Key)} mm²")) +
            ". Sections des départs pour la puissance, 0,75 mm² pour la commande et les E/S câblées (voies de réserve non câblées).",
            Reference);
        var manuelles = c.Goulottes.Where(x => x.Manuelle).ToList();
        if (manuelles.Count > 0)
            note.Info("Goulottes ajoutées à la main", $"{manuelles.Count} goulotte(s), {N(manuelles.Sum(x => x.Longueur) / 1000, 1)} m : " +
                string.Join(", ", manuelles.GroupBy(x => x.Modele).Select(g => $"{g.Count()} × {g.Key.Libelle}")) + ".", Reference);
        foreach (var g in c.Goulottes.Where(x => !x.Manuelle).GroupBy(x => (x.Verticale, x.Modele)))
        {
            var max = g.Max(x => x.Remplissage);
            var nom = g.Key.Verticale ? "Goulottes verticales" : "Goulottes horizontales";
            note.Verifier($"{nom} {g.Key.Modele.Libelle}", max <= RemplissageMax + 1e-6,
                $"{g.Count()} tronçon(s), {N(g.Sum(x => x.Verticale ? x.Hauteur : x.Largeur) / 1000, 1)} m ; remplissage le plus fort {N(max, 0)} % " +
                $"(≤ {N(RemplissageMax, 0)} %)" + (max <= RemplissageMax + 1e-6 ? "." : " : doubler la goulotte ou répartir les fils."),
                Reference, StatutCalcul.Alerte);
        }
        note.Info("Séparation des fils", "Puissance et commande dans des goulottes distinctes ou séparées par une cloison ; " +
            "fils des variateurs dans la goulotte qui longe leur bloc.", Reference);
    }
}
