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

    /// <summary>Longueur de câble moteur blindé au-delà de laquelle une self de sortie est à prévoir, en m.</summary>
    public const double LongueurSelfSortie = 50;

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
        if (r.Armoire is not { } principale) return;
        foreach (var a in principale.Enveloppes)
            UneEnveloppe(r, a, l, fabricant, principale.Coffrets.Count > 0 ? a.Nom : "");
    }

    /// <summary>Une enveloppe, sa plaque, ses accessoires et sa solution thermique ; nom précisé quand il y a des coffrets déportés.</summary>
    static void UneEnveloppe(ResultatProjet r, ResultatArmoire a, List<LigneDevis> l, string fabricant, string nom)
    {
        var sol = a.Type == "Armoire au sol";
        // L'enveloppe et ses accessoires viennent du fabricant d'enveloppes retenu (Rittal, Legrand ou Schneider).
        var marque = a.Marque.Length > 0 ? $"{a.Marque} – {a.Gamme}" : fabricant;
        fabricant = a.Marque.Length > 0 ? a.Marque : fabricant;
        l.Add(new(Enveloppe, sol ? "Armoire électrique au sol" : "Coffret électrique mural",
            $"{N(a.Largeur)} × {N(a.Hauteur)} × {N(a.Profondeur)} mm, {CalculThermique.LibelleMateriau(r.Projet.Armoire.Materiau)}, IP55",
            marque, a.Reference, a.Cellules, "u", nom,
            string.Join(" ; ", new[] { a.Cellules > 1 ? "Cellules juxtaposables" : "", a.Id is null ? "" : "Coffret déporté" }.Where(t => t.Length > 0))));
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

        if (a.Thermique is not { } t) return;
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
            .Where(e => e.Famille is not ("Câble" or "Moteur" or "Récepteur" or CalculProjet.SourceManquante))
            .GroupBy(e => e.Reference.Length > 0 ? $"{e.Fabricant}|{e.Reference}" : $"|{e.Designation}|{e.Calibre}");
        var elementsArbre = r.Arbre.Parcourir().ToList();
        foreach (var g in articles)
        {
            var e = g.First();
            var source = elementsArbre.FirstOrDefault(x => x.Repere == e.Repere.Split(", ")[0]) ?? e;
            l.Add(new(Appareillage, e.Designation, Caracteristiques(source, r), e.Fabricant, e.Reference, g.Sum(x => x.Quantite), "u",
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
        var verrouillages = r.Departs.Count(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.Inverseur or ModeDemarrage.EtoileTriangle or ModeDemarrage.DeuxVitesses });
        if (verrouillages > 0)
            l.Add(new(Accessoires, "Verrouillage mécanique entre contacteurs", "inverseurs, étoile-triangle et deux vitesses", fabricant, "", verrouillages, "u"));
        var etoileTriangle = r.Departs.Count(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle });
        if (etoileTriangle > 0)
            l.Add(new(Accessoires, "Temporisation étoile-triangle", "bloc additif ou relais temporisé", fabricant, "", etoileTriangle, "u"));
        if (elements.Any(e => e.RepereOrigine == "QS0"))
            l.Add(new(Accessoires, "Commande rotative extérieure cadenassable", "poignée rouge sur fond jaune (arrêt d'urgence)", fabricant, "", 1, "u", "QS0",
                "NF EN 60204-1, 5.3"));
        var modulaires = elements.Count(e => e.Famille == "Disjoncteur" && e.Courant is > 0 and <= 63);
        if (modulaires > 1)
            l.Add(new(Accessoires, "Peigne de raccordement", "pour disjoncteurs modulaires", fabricant, "",
                Math.Max(1, r.Armoire?.Bandes.Count(b => b.Zone == ZoneCem.Intermediaire) ?? 1), "u", Remarque: Estimation));
        var variateurs = elements.Where(e => e.Famille == "Variateur").ToList();
        if (variateurs.Count > 0)
        {
            // Seuls les accessoires que le variateur n'apporte pas sont chiffrés.
            var sansFiltre = variateurs.Where(v => !v.FiltreCemIntegre).ToList();
            if (sansFiltre.Count > 0)
                l.Add(new(Accessoires, "Filtre CEM d'entrée variateur", "catégorie C2, côté réseau", fabricant, "", sansFiltre.Count, "u",
                    Reperes(sansFiltre), "Filtre non intégré au variateur retenu"));
            var sansBlindage = variateurs.Where(v => !v.BlindageFourni).ToList();
            if (sansBlindage.Count > 0)
                l.Add(new(Accessoires, "Collier de reprise de blindage à 360°", "pour câble moteur blindé", fabricant, "", 2 * sansBlindage.Count, "u",
                    Reperes(sansBlindage), "Kit de blindage non livré avec le variateur : barre de reprise et sortie variateur"));
            var servos = r.Departs.Where(d => d.Depart is DepartMoteur { Demarrage: ModeDemarrage.Servomoteur }).ToList();
            if (servos.Count > 0)
                l.Add(new(Accessoires, "Résistance de freinage externe", "selon le cycle et l'énergie de freinage", fabricant, "", servos.Count, "u",
                    string.Join(", ", servos.Select(d => $"U{d.Depart.Numero}")), "Si la résistance interne du servovariateur ne suffit pas"));
            // Au-delà de cette longueur, les capacités du câble blindé imposent en général une self de sortie ou un filtre dv/dt.
            foreach (var d in r.Departs.Where(d => d.Depart is DepartMoteur { SurVariateur: true } m && m.Cable.Longueur > LongueurSelfSortie))
                l.Add(new(Accessoires, "Self de sortie moteur / filtre dv/dt", $"{N(d.CourantEmploi)} A, 400 V", fabricant, "", 1, "u", $"{d.Depart.Code}",
                    $"Câble blindé de {N(d.Depart.Cable.Longueur)} m > {N(LongueurSelfSortie)} m : à confirmer selon la notice du variateur"));
        }
    }

    /// <summary>
    /// Caractéristiques d'un appareil pour le fournisseur, suffisantes pour chiffrer sans référence :
    /// calibre, pôles, courbe, plage de réglage, puissance du récepteur, tension de bobine.
    /// </summary>
    public static string Caracteristiques(ElementMateriel e, ResultatProjet r)
    {
        var parts = new List<string>();
        if (e.Calibre.Length > 0) parts.Add(e.Calibre);
        if (e.Poles > 0 && !System.Text.RegularExpressions.Regex.IsMatch(e.Calibre, @"\dP"))
            parts.Add($"{e.Poles}P");
        if (e.Courbe.Length > 0 && !e.Calibre.Contains("courbe")) parts.Add($"courbe {e.Courbe}");
        if (e.PlageMax > 0 && !e.Calibre.Contains('–')) parts.Add($"réglage {N(e.PlageMin)}–{N(e.PlageMax)} A");
        if (e.TailleFusible.Length > 0 && !e.Calibre.Contains(e.TailleFusible)) parts.Add($"taille {e.TailleFusible}");
        var depart = e.DepartId is { } id ? r.Departs.FirstOrDefault(d => d.Depart.Id == id)?.Depart : null;
        if (e.Famille is "Contacteur" or "Disjoncteur moteur" or "Relais thermique" or "Variateur" or "Démarreur progressif" or "Disjoncteur")
        {
            var u = depart?.Alimentation switch { TypeAlimentation.Mono230 => "230 V", null => "", _ => "400 V" };
            switch (depart)
            {
                case DepartMoteur m:
                    parts.Add($"moteur {N(m.PuissanceKw)} kW {u}");
                    break;
                case DepartResistif rs:
                    parts.Add($"charge résistive {N(rs.PuissanceKw)} kW {u}");
                    break;
            }
        }
        if (e.Famille == "Contacteur" && !e.Designation.Contains("bobine"))
            parts.Add($"bobine {SourcesCommande.Libelle(r.Projet.TensionBobines)}");
        if (e.Famille is "Alimentation" or "Variateur" or "Démarreur progressif" && e.PuissanceKw > 0 && !string.Join(" ", parts).Contains("kW"))
            parts.Add($"{N(e.PuissanceKw)} kW");
        return string.Join(", ", parts.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct());
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

        if (r.Armoire is not { } principale) return;
        // Armoire principale et coffrets déportés additionnés.
        var enveloppes = principale.Enveloppes.ToList();
        var rail = enveloppes.Sum(a => a.LongueurRail);
        var cellules = enveloppes.Sum(a => a.Cellules);
        l.Add(new(Montage, "Rail DIN symétrique 35 mm", "longueur 2 m", fabricant, "", Math.Ceiling(rail / 2), "u",
            Remarque: $"{N(rail, 1)} m nécessaires"));
        foreach (var g in enveloppes.SelectMany(a => a.Cablage.Goulottes).GroupBy(x => x.Modele).OrderBy(g => g.Key.Section))
        {
            var metres = g.Sum(x => x.Longueur) / 1000;
            l.Add(new(Montage, "Goulotte de câblage à fentes avec couvercle", $"{g.Key.Libelle}, longueur 2 m", fabricant, "",
                Math.Ceiling(metres * 1.1 / 2), "u", Remarque: $"{N(metres, 1)} m nécessaires, 10 % de chutes"));
        }
        l.Add(new(Montage, "Barrette de terre / collecteur PE", "cuivre, avec bornes", fabricant, "", cellules, "u"));
        if (r.Departs.Any(d => d.Depart is DepartMoteur { SurVariateur: true }))
            l.Add(new(Montage, "Barre de reprise de blindage", "cuivre, avec supports", fabricant, "", cellules, "u"));

        // Filerie interne estimée par appareil implanté, à la longueur moyenne d'un fil sur la plaque de son enveloppe.
        var fils = enveloppes.SelectMany(a => a.Cablage.Fils.Select(f => (Fil: f, a.Cablage.LongueurMoyenne))).ToList();
        foreach (var f in fils.GroupBy(f => (f.Fil.Section, f.Fil.Usage)).OrderBy(g => g.Key.Usage).ThenByDescending(g => g.Key.Section))
        {
            var nombre = f.Sum(x => x.Fil.Nombre);
            var metres = f.Sum(x => x.Fil.Nombre * x.LongueurMoyenne);
            var (designation, type) = f.Key.Usage switch
            {
                "Terre" => ("Fil de câblage vert/jaune (PE)", "H07V-K"),
                "Commande" => ("Fil de câblage bleu foncé (commande 24 V)", f.Key.Section < 1 ? "H05V-K" : "H07V-K"),
                _ => ("Fil de câblage noir (puissance)", "H07V-K"),
            };
            l.Add(new(Montage, designation, $"{type} {N(f.Key.Section)} mm²", "", "", Math.Ceiling(metres * 1.15), "m",
                Remarque: $"{nombre} fils × {N(metres / Math.Max(1, nombre), 1)} m + 15 % ; estimation"));
        }
        foreach (var g in fils.GroupBy(f => f.Fil.Section).OrderBy(g => g.Key))
            l.Add(new(Montage, "Embouts de câblage isolés", $"{N(g.Key)} mm²", "", "", Math.Ceiling(2 * g.Sum(f => f.Fil.Nombre) * 1.1), "u",
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
            var blinde = dep is DepartMoteur { SurVariateur: true };
            var nombre = dep is DepartMoteur { Demarrage: ModeDemarrage.EtoileTriangle or ModeDemarrage.DeuxVitesses } ? 2 : 1;
            var conducteurs = Conducteurs(dep.Alimentation);
            var designation = blinde ? "Câble moteur blindé cuivre (CEM), tresse cuivre ≥ 85 %" : $"Câble cuivre {dep.Cable.Isolant}";
            var composition = blinde ? $"{conducteurs}G{N(d.Section)} mm² + blindage" : $"{conducteurs}G{N(d.Section)} mm²";
            cables.Add((designation, composition, nombre * dep.Cable.Longueur, d.Section, blinde, $"W{dep.Numero}"));
            // Servomoteur : câble codeur blindé préconfectionné, de la même longueur que le câble moteur.
            if (dep is DepartMoteur { Demarrage: ModeDemarrage.Servomoteur })
                cables.Add(("Câble codeur blindé préconfectionné", "selon la gamme du servovariateur", dep.Cable.Longueur, 0.5, true, $"W{dep.Numero}.C"));
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
        // Le blindage d'un câble de variateur est repris à 360° aux deux bouts : presse-étoupe CEM aussi à la boîte à bornes du moteur.
        foreach (var g in cables.Where(c => c.Blinde).GroupBy(c => PresseEtoupe(c.Section)).OrderBy(g => g.Key))
            l.Add(new(Cables, "Presse-étoupe CEM laiton côté moteur", $"{g.Key}, IP68", "", "", g.Count(), "u",
                string.Join(", ", g.Select(c => c.Repere)), "Reprise du blindage à la boîte à bornes du moteur"));
        // Barrières et scrutateurs : un câble de raccordement par connecteur, longueur selon l'implantation sur la machine.
        var optiques = r.Securite?.Appareils.SelectMany(a => a.Parcourir()).Where(e => e.Famille == "Capteur de sécurité").ToList() ?? new();
        var connecteurs = new List<(string Connectique, string Repere)>();
        foreach (var e in optiques)
            foreach (var c in e.RepereOrigine.StartsWith("BI") ? new[] { "M12 5 broches (émetteur)", "M12 8 broches (récepteur)" } : new[] { "M12 / connecteur système 12 broches" })
                connecteurs.Add((c, e.Repere));
        foreach (var g in connecteurs.GroupBy(c => c.Connectique))
            l.Add(new(Cables, "Câble de raccordement capteur de sécurité, connecteur femelle droit", g.Key, "", "", g.Count(), "u",
                string.Join(", ", g.Select(c => c.Repere).Distinct()), "Longueur selon l'implantation sur la machine"));
        if (connecteurs.Count > 0)
            l.Add(new(Cables, "Presse-étoupe polyamide (câbles des barrières et scrutateurs)", "M16 / M20, IP68", "", "", connecteurs.Count, "u",
                Remarque: "Ou traversée de connecteur M12 en paroi d'armoire"));
        var (commande, _) = CalculArmoire.BornesCommande(r.Projet);
        if (commande > 0)
        {
            // Les bornes des barrières et scrutateurs ont déjà leurs câbles M12.
            var bornesOptiques = optiques.Sum(e => e.RepereOrigine.StartsWith("BI") ? 9 : 8);
            var cablesCommande = (int)Math.Ceiling(Math.Max(0, commande - bornesOptiques) / 8.0);
            l.Add(new(Cables, "Presse-étoupe polyamide (câbles de commande)", "M20, IP68", "", "", cablesCommande, "u", Remarque: Estimation));
        }
    }

    static int Conducteurs(TypeAlimentation a) => a switch { TypeAlimentation.Mono230 or TypeAlimentation.Bi400 => 3, TypeAlimentation.Tri400 => 4, _ => 5 };

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
