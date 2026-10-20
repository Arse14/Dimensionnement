using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Materiel;

/// <summary>Une ligne de la demande de devis.</summary>
public record LigneDevis(string Categorie, string Designation, string Caracteristiques, string Fabricant, string Reference,
    double Quantite, string Unite, string Reperes = "", string Remarque = "");

/// <summary>
/// Liste exhaustive du matériel à chiffrer : enveloppe et thermique, appareillage, accessoires, borniers,
/// montage et câblage, câbles en mètres et presse-étoupes. Les quantités estimées sont signalées en remarque.
/// </summary>
public static class ListeDevis
{
    public const string Enveloppe = "1. Enveloppe et thermique";
    public const string Appareillage = "2. Appareillage";
    public const string Accessoires = "3. Accessoires d'appareillage";
    public const string Borniers = "4. Borniers";
    public const string Montage = "5. Montage et câblage interne";
    public const string Cables = "6. Câbles et entrées de câbles";

    /// <summary>Marge sur les longueurs de câble (chutes, lovage).</summary>
    public const double MargeCable = 0.10;

    const string Estimation = "Quantité estimée";

    public static IReadOnlyList<LigneDevis> Generer(ResultatProjet r)
    {
        var lignes = new List<LigneDevis>();
        var fabricant = r.Projet.References.Fabricant.ToString();
        EnveloppeEtThermique(r, lignes, fabricant);
        AppareillageEtAccessoires(r, lignes, fabricant);
        BorniersEtMontage(r, lignes, fabricant);
        CablesEtEntrees(r, lignes);
        return lignes;
    }

    static void EnveloppeEtThermique(ResultatProjet r, List<LigneDevis> l, string fabricant)
    {
        if (r.Armoire is not { } a) return;
        var sol = a.Type == "Armoire au sol";
        // L'enveloppe et ses accessoires viennent du fabricant d'enveloppes retenu (Rittal, Legrand ou Schneider).
        var marque = a.Marque.Length > 0 ? $"{a.Marque} – {a.Gamme}" : fabricant;
        fabricant = a.Marque.Length > 0 ? a.Marque : fabricant;
        l.Add(new(Enveloppe, sol ? "Armoire électrique au sol" : "Coffret électrique mural",
            $"{N(a.Largeur)} × {N(a.Hauteur)} × {N(a.Profondeur)} mm, {CalculThermique.LibelleMateriau(r.Projet.Armoire.Materiau)}, IP55",
            marque, a.Reference, a.Cellules, "u", Remarque: a.Cellules > 1 ? "Cellules juxtaposables" : ""));
        l.Add(new(Enveloppe, "Plaque de montage", $"surface utile {N(a.PlaqueLargeur)} × {N(a.PlaqueHauteur)} mm", marque, a.ReferencePlaque, a.Cellules, "u"));
        if (sol)
        {
            l.Add(new(Enveloppe, "Socle", $"hauteur 100 mm, largeur {N(a.Largeur)} mm", fabricant, "", a.Cellules, "u"));
            if (a.Cellules > 1)
                l.Add(new(Enveloppe, "Kit de juxtaposition", "", fabricant, "", a.Cellules - 1, "u"));
            l.Add(new(Enveloppe, "Éclairage d'armoire LED avec contact de porte", "", fabricant, "", a.Cellules, "u"));
        }
        l.Add(new(Enveloppe, "Plaque passe-câbles / bride d'entrée", "bas d'enveloppe", fabricant, "", a.Cellules, "u"));
        l.Add(new(Enveloppe, "Pochette à schémas", "format A4", fabricant, "", 1, "u"));

        if (r.Thermique is not { } t) return;
        switch (t.Solution)
        {
            case SolutionThermique.Ventilation:
                l.Add(new(Enveloppe, "Ventilateur à filtre", $"débit libre {N(t.Ventilateur, 0)} m³/h, 230 V", fabricant, "", 1, "u",
                    Remarque: $"Besoin {N(t.DebitNecessaire, 0)} m³/h"));
                l.Add(new(Enveloppe, "Grille de sortie à filtre", "même taille que le ventilateur", fabricant, "", 1, "u"));
                l.Add(new(Enveloppe, "Thermostat d'armoire", "contact NO, réglable", fabricant, "", 1, "u"));
                break;
            case SolutionThermique.Climatisation:
                l.Add(new(Enveloppe, "Climatiseur d'armoire", $"puissance frigorifique {N(t.Climatiseur, 0)} W (L35 L35)", fabricant, "", 1, "u",
                    Remarque: $"Besoin {N(t.PuissanceFrigorifique, 0)} W"));
                break;
        }
        if (t.Resistance > 0)
        {
            l.Add(new(Enveloppe, "Résistance de chauffage anticondensation", $"{N(t.Resistance, 0)} W, 230 V", fabricant, "", 1, "u"));
            l.Add(new(Enveloppe, "Thermostat et hygrostat de chauffage", "contact NF", fabricant, "", 1, "u"));
        }
    }

    static void AppareillageEtAccessoires(ResultatProjet r, List<LigneDevis> l, string fabricant)
    {
        // Une ligne par référence : le fournisseur chiffre un article, quels que soient les repères qui l'utilisent.
        var articles = r.Nomenclature
            .Where(e => e.Famille is not ("Câble" or "Moteur" or "Récepteur"))
            .GroupBy(e => e.Reference.Length > 0 ? $"{e.Fabricant}|{e.Reference}" : $"|{e.Designation}|{e.Calibre}");
        foreach (var g in articles)
        {
            var e = g.First();
            l.Add(new(Appareillage, e.Designation, e.Calibre, e.Fabricant, e.Reference, g.Sum(x => x.Quantite), "u",
                string.Join(", ", g.Select(x => x.Repere)), e.Reference.Length == 0 ? "Référence à compléter" : ""));
        }

        var elements = r.Arbre.Parcourir().ToList();
        var disjMoteurs = elements.Where(e => e.Famille == "Disjoncteur moteur").ToList();
        if (disjMoteurs.Count > 0)
        {
            l.Add(new(Accessoires, "Contact auxiliaire de disjoncteur moteur", "1 NO + 1 NF, signalisation de position", fabricant, "",
                disjMoteurs.Count, "u", Reperes(disjMoteurs)));
            var avecContacteur = disjMoteurs.Where(d => d.Enfants.Any(c => c.Famille == "Contacteur")).ToList();
            if (avecContacteur.Count > 0)
                l.Add(new(Accessoires, "Bloc de liaison disjoncteur moteur / contacteur", "", fabricant, "", avecContacteur.Count, "u",
                    Reperes(avecContacteur)));
        }
        var verrouillages = r.Departs.Count(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.Inverseur or ModeDemarrage.EtoileTriangle });
        if (verrouillages > 0)
            l.Add(new(Accessoires, "Verrouillage mécanique entre contacteurs", "inverseurs et étoile-triangle", fabricant, "", verrouillages, "u"));
        var etoileTriangle = r.Departs.Count(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle });
        if (etoileTriangle > 0)
            l.Add(new(Accessoires, "Temporisation étoile-triangle", "bloc additif ou relais temporisé", fabricant, "", etoileTriangle, "u"));
        if (elements.Any(e => e.Repere == "QS0"))
            l.Add(new(Accessoires, "Commande rotative extérieure cadenassable", "poignée rouge sur fond jaune (arrêt d'urgence)", fabricant, "", 1, "u", "QS0",
                "NF EN 60204-1, 5.3"));
        var modulaires = elements.Count(e => e.Famille == "Disjoncteur" && e.Courant is > 0 and <= 63);
        if (modulaires > 1)
            l.Add(new(Accessoires, "Peigne de raccordement", "pour disjoncteurs modulaires", fabricant, "",
                Math.Max(1, r.Armoire?.Bandes.Count(b => b.Zone == ZoneCem.Intermediaire) ?? 1), "u", Remarque: Estimation));
        var variateurs = elements.Count(e => e.Famille == "Variateur");
        if (variateurs > 0)
        {
            l.Add(new(Accessoires, "Filtre CEM d'entrée variateur", "si non intégré au variateur", fabricant, "", variateurs, "u",
                Remarque: "Selon catégorie CEM C2 / C3 visée"));
            l.Add(new(Accessoires, "Collier de reprise de blindage à 360°", "pour câble moteur blindé", fabricant, "", 2 * variateurs, "u"));
        }
    }

    static void BorniersEtMontage(ResultatProjet r, List<LigneDevis> l, string fabricant)
    {
        var bornes = CalculArmoire.BornesPuissance(r).ToList();
        foreach (var g in bornes.GroupBy(b => b.Section).OrderBy(g => g.Key))
        {
            var phases = g.Sum(b => b.Nombre - 1);
            l.Add(new(Borniers, "Borne de passage à vis", $"{N(g.Key)} mm², {N(Encombrements.LargeurBorne(g.Key))} mm", fabricant, "", phases, "u", "X1"));
            l.Add(new(Borniers, "Borne de terre (vert/jaune)", $"{N(g.Key)} mm²", fabricant, "", g.Count(), "u", "X1"));
        }
        var (commande, detail) = CalculArmoire.BornesCommande(r.Projet);
        if (commande > 0)
            l.Add(new(Borniers, "Borne de passage à ressort", $"2,5 mm², {N(Encombrements.LargeurBorneCommande)} mm", fabricant, "", commande, "u", "X2", detail));
        var borniers = (bornes.Count > 0 ? 1 : 0) + (commande > 0 ? 1 : 0);
        if (borniers > 0)
        {
            l.Add(new(Borniers, "Flasque et butée d'extrémité", "", fabricant, "", 2 * borniers, "jeu"));
            l.Add(new(Borniers, "Repères de bornes", "planches à imprimer", fabricant, "", borniers, "lot"));
        }

        if (r.Armoire is not { } a) return;
        l.Add(new(Montage, "Rail DIN symétrique 35 mm", "longueur 2 m", fabricant, "", Math.Ceiling(a.LongueurRail / 2), "u",
            Remarque: $"{N(a.LongueurRail, 1)} m nécessaires"));
        foreach (var g in a.Cablage.Goulottes.GroupBy(x => x.Modele).OrderBy(g => g.Key.Section))
        {
            var metres = g.Sum(x => x.Longueur) / 1000;
            l.Add(new(Montage, "Goulotte de câblage à fentes avec couvercle", $"{g.Key.Libelle}, longueur 2 m", fabricant, "",
                Math.Ceiling(metres * 1.1 / 2), "u", Remarque: $"{N(metres, 1)} m nécessaires, 10 % de chutes"));
        }
        l.Add(new(Montage, "Barrette de terre / collecteur PE", "cuivre, avec bornes", fabricant, "", a.Cellules, "u"));
        if (r.Departs.Any(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.Variateur }))
            l.Add(new(Montage, "Barre de reprise de blindage", "cuivre, avec supports", fabricant, "", a.Cellules, "u"));

        // Filerie interne estimée par appareil implanté, à la longueur moyenne d'un fil sur la plaque.
        var longueur = a.Cablage.LongueurMoyenne;
        foreach (var f in a.Cablage.Fils.GroupBy(f => (f.Section, f.Usage)).OrderBy(g => g.Key.Usage).ThenByDescending(g => g.Key.Section))
        {
            var nombre = f.Sum(x => x.Nombre);
            var (designation, type) = f.Key.Usage switch
            {
                "Terre" => ("Fil de câblage vert/jaune (PE)", "H07V-K"),
                "Commande" => ("Fil de câblage bleu foncé (commande 24 V)", f.Key.Section < 1 ? "H05V-K" : "H07V-K"),
                _ => ("Fil de câblage noir (puissance)", "H07V-K"),
            };
            l.Add(new(Montage, designation, $"{type} {N(f.Key.Section)} mm²", "", "", Math.Ceiling(nombre * longueur * 1.15), "m",
                Remarque: $"{nombre} fils × {N(longueur, 1)} m + 15 % ; estimation"));
        }
        foreach (var g in a.Cablage.Fils.GroupBy(f => f.Section).OrderBy(g => g.Key))
            l.Add(new(Montage, "Embouts de câblage isolés", $"{N(g.Key)} mm²", "", "", Math.Ceiling(2 * g.Sum(f => f.Nombre) * 1.1), "u",
                Remarque: "2 par fil + 10 %"));
        l.Add(new(Montage, "Repères d'appareils et de fils", "étiquettes", "", "", 1, "lot"));
    }

    static void CablesEtEntrees(ResultatProjet r, List<LigneDevis> l)
    {
        var cables = new List<(string Designation, string Caracteristiques, double Longueur, double Section, bool Blinde, string Repere)>();
        var reseau = r.Projet.Reseau;
        var conducteursArrivee = Conducteurs(reseau.Alimentation);
        cables.Add(($"Câble d'arrivée cuivre {reseau.CableArrivee.Isolant}", $"{conducteursArrivee}G{N(r.SectionArrivee)} mm²",
            reseau.CableArrivee.Longueur, r.SectionArrivee, false, "W0"));
        foreach (var d in r.Departs.Where(d => d.Depart is not DepartAlimentation24V))
        {
            var dep = d.Depart;
            var blinde = dep is DepartMoteur { Demarrage: ModeDemarrage.Variateur };
            var nombre = dep is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle } ? 2 : 1;
            var conducteurs = Conducteurs(dep.Alimentation);
            var designation = blinde ? "Câble moteur blindé cuivre (CEM)" : $"Câble cuivre {dep.Cable.Isolant}";
            cables.Add((designation, $"{conducteurs}G{N(d.Section)} mm²", nombre * dep.Cable.Longueur, d.Section, blinde, $"W{dep.Numero}"));
        }
        foreach (var g in cables.GroupBy(c => (c.Designation, c.Caracteristiques)))
        {
            var longueur = Math.Ceiling(g.Sum(c => c.Longueur) * (1 + MargeCable));
            l.Add(new(Cables, g.Key.Designation, g.Key.Caracteristiques, "", "", longueur, "m", string.Join(", ", g.Select(c => c.Repere)),
                $"{N(MargeCable * 100, 0)} % de marge inclus"));
        }

        foreach (var g in cables.GroupBy(c => (Taille: PresseEtoupe(c.Section), c.Blinde)).OrderBy(g => g.Key.Taille))
            l.Add(new(Cables, g.Key.Blinde ? "Presse-étoupe CEM laiton" : "Presse-étoupe polyamide", $"{g.Key.Taille}, IP68", "", "", g.Count(), "u",
                string.Join(", ", g.Select(c => c.Repere)), "Taille selon le diamètre réel du câble"));
        var (commande, _) = CalculArmoire.BornesCommande(r.Projet);
        if (commande > 0)
        {
            var cablesCommande = (int)Math.Ceiling(commande / 8.0);
            l.Add(new(Cables, "Presse-étoupe polyamide (câbles de commande)", "M20, IP68", "", "", cablesCommande, "u", Remarque: Estimation));
        }
    }

    static int Conducteurs(TypeAlimentation a) => a switch { TypeAlimentation.Mono230 => 3, TypeAlimentation.Tri400 => 4, _ => 5 };

    /// <summary>Taille de presse-étoupe usuelle d'un câble multiconducteur selon sa section.</summary>
    public static string PresseEtoupe(double section) => section switch
    {
        <= 2.5 => "M20",
        <= 6 => "M25",
        <= 16 => "M32",
        <= 35 => "M40",
        <= 70 => "M50",
        _ => "M63",
    };

    static string Reperes(IEnumerable<ElementMateriel> e) => string.Join(", ", e.Select(x => x.Repere));
}
