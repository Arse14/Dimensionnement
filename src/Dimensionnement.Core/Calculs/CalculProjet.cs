using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public class ResultatProjet
{
    public required Projet Projet { get; init; }
    public required IReadOnlyList<ResultatDepart> Departs { get; init; }

    /// <summary>Note de calcul de l'arrivée : bilan de puissance, appareils de tête et câble d'arrivée.</summary>
    public required NoteDeCalcul NoteArrivee { get; init; }

    public double PuissanceActive { get; init; }
    public double PuissanceReactive { get; init; }
    public double PuissanceApparente { get; init; }
    public double CosPhiGlobal { get; init; }
    public double CoefficientSimultaneite { get; init; }
    public double CourantEmploi { get; init; }
    public double CalibreGeneral { get; init; }
    public double SectionArrivee { get; init; }
    public double ChuteTensionArrivee { get; init; }

    /// <summary>Consommateurs 24 V DC retenus pour dimensionner les alimentations de commande.</summary>
    public IReadOnlyList<ConsommationCommande> Consommateurs24V { get; init; } = Array.Empty<ConsommationCommande>();
    public double Consommation24V => Consommateurs24V.Sum(c => c.Courant);

    /// <summary>Arbre du matériel, de l'arrivée réseau jusqu'à chaque récepteur.</summary>
    public required ElementMateriel Arbre { get; init; }

    public ResultatSecurite? Securite { get; init; }
    public ResultatAutomate? Automate { get; init; }
    public IReadOnlyList<PointControle> Controles { get; init; } = Array.Empty<PointControle>();
    public ResultatReferences? References { get; init; }
    public ResultatSelectivite? Selectivite { get; set; }
    public ResultatArmoire? Armoire { get; set; }
    public ResultatThermique? Thermique { get; set; }

    /// <summary>Toutes les notes de calcul du projet : départs, arrivée, sécurité, automate.</summary>
    public IEnumerable<NoteDeCalcul> Notes => Departs.Select(d => d.Note).Append(NoteArrivee)
        .Concat(new[] { Securite?.Note, Automate?.Note, References?.Note, Selectivite?.Note, Armoire?.Note, Thermique?.Note }.OfType<NoteDeCalcul>());

    public StatutCalcul StatutGlobal => Notes.Select(n => n.StatutGlobal).Max();

    public int NombreAlertes => Notes.SelectMany(n => n.Lignes)
        .Count(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme);

    /// <summary>Nomenclature : appareils de l'arbre regroupés par désignation et calibre.</summary>
    public IReadOnlyList<ElementMateriel> Nomenclature => Arbre.Parcourir()
        .Where(e => e.Famille != "Réseau")
        .GroupBy(e => (e.Famille, e.Designation, e.Calibre, e.Fabricant, e.Reference))
        .Select(g => new ElementMateriel
        {
            Repere = string.Join(", ", g.Select(e => e.Repere)),
            Famille = g.Key.Famille, Designation = g.Key.Designation, Calibre = g.Key.Calibre,
            Fabricant = g.Key.Fabricant, Reference = g.Key.Reference,
            Quantite = g.Sum(e => e.Quantite),
        })
        .ToList();
}

/// <summary>Un poste du bilan 24 V DC, en A.</summary>
public record ConsommationCommande(string Designation, double Courant);

/// <summary>Calcule tout le projet : les départs, le bilan de puissance et l'arrivée.</summary>
public static class CalculProjet
{
    const string RefBilan = "Guide UTE C 15-105, coefficients d'utilisation et de simultanéité";

    public static ResultatProjet Calculer(Projet projet) => Calculer(projet, CatalogueAutomates.Charger());

    public static ResultatProjet Calculer(Projet projet, IReadOnlyList<CpuAutomate> catalogue,
        IReadOnlyList<ReferenceCatalogue>? references = null)
    {
        var reseau = projet.Reseau;

        // Le bilan ne dépend pas des câbles : un premier passage donne les puissances, le second
        // recalcule les départs avec la chute de tension consommée par le câble d'arrivée.
        var consommateurs24V = Consommateurs24V(projet);
        var consommation24V = consommateurs24V.Sum(c => c.Courant);
        var premierPassage = projet.Departs.Select(d => CalculDepart.Calculer(d, reseau, 0, consommation24V)).ToList();

        var note = new NoteDeCalcul("ARR", projet.ForcagesArrivee);
        var p = premierPassage.Sum(r => r.PuissanceActive * r.Depart.CoefficientUtilisation);
        var q = premierPassage.Sum(r => r.PuissanceReactive * r.Depart.CoefficientUtilisation);
        note.Info("Puissances utilisées", $"Σ (P × ku) = {N(p)} kW ; Σ (Q × ku) = {N(q)} kvar", RefBilan);

        var ksTable = Simultaneite(projet.Departs.Count);
        var ks = note.Valeur(CodesCalcul.Simultaneite, "Coefficient de simultanéité", ksTable,
            ksTable, "",
            "ks selon le nombre de départs", $"{projet.Departs.Count} départ(s)", RefBilan);

        var s = ks * Math.Sqrt(p * p + q * q);
        var cosPhi = s > 0 ? ks * p / s : 1;
        var mono = reseau.Alimentation == TypeAlimentation.Mono230;
        var ib = s * 1000 / (mono ? 230 : Math.Sqrt(3) * 400);
        note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi de l'armoire", ib, ib, "A",
            mono ? "Ib = S / U" : "Ib = S / (√3 × U)",
            $"S = ks × √(P² + Q²) = {N(s)} kVA, cos φ global = {N(cosPhi)}", RefBilan);
        if (projet.Departs.Any(d => d.Alimentation == TypeAlimentation.Mono230) && !mono)
            note.Info("Équilibrage des phases",
                "Les départs monophasés sont supposés répartis sur les trois phases ; vérifier l'équilibrage.");

        var qs = note.Valeur(CodesCalcul.CalibreSectionneur, "Interrupteur-sectionneur général", ib,
            Calibres.Superieur(Calibres.InterrupteursSectionneurs, ib) ?? ib, "A", "Ith ≥ Ib",
            $"Ib = {A(ib)}", "NF EN 60204-1, 5.3 ; NF EN 60947-3");
        var qg = note.Valeur(CodesCalcul.CalibreGeneral, "Disjoncteur général", ib,
            Calibres.Superieur(Calibres.Disjoncteurs, ib) ?? ib, "A", "In ≥ Ib",
            $"Ib = {A(ib)}", "NF C 15-100, 433");
        note.Info("Pôles du général", CalculDepart.Poles(reseau.Alimentation, reseau.Slt), "NF C 15-100, 431");
        note.Info("Pouvoir de coupure", $"Pouvoir de coupure ≥ {N(reseau.IccKiloAmperes)} kA.", "NF EN 60947-2");
        note.Verifier("Sectionneur et disjoncteur général", qs >= qg,
            $"Interrupteur-sectionneur {A(qs)}, disjoncteur général {A(qg)}", siEchec: StatutCalcul.Alerte);

        var cable = CalculCable.Dimensionner(note, reseau.CableArrivee, reseau.Alimentation, qg, ib, cosPhi, reseau, 0);

        var departs = projet.Departs.Select(d => CalculDepart.Calculer(d, reseau, cable.ChuteTension, consommation24V)).ToList();
        if (consommation24V > 0)
        {
            note.Info("Bilan 24 V DC", $"Consommation des circuits de commande : {A(consommation24V)}.", "Bilan 24 V DC du projet");
            note.Verifier("Alimentation 24 V", projet.Departs.OfType<DepartAlimentation24V>().Any(),
                "Le projet consomme du 24 V DC : ajouter un départ « Alimentation 24 V DC ».", siEchec: StatutCalcul.Alerte);
        }
        var securite = CalculSecurite.Calculer(projet, departs);
        var automate = CalculAutomate.Calculer(projet.Automate, catalogue);

        var arbre = new ElementMateriel { Repere = "Réseau", Famille = "Réseau",
            Designation = $"Arrivée {Libelle(reseau.Alimentation)}, {reseau.Slt}", Calibre = $"Icc {N(reseau.IccKiloAmperes)} kA" };
        var conducteurs = reseau.Alimentation switch { TypeAlimentation.Mono230 => 3, TypeAlimentation.Tri400 => 4, _ => 5 };
        var tete = arbre
            .Ajouter(new ElementMateriel { Repere = "W0", Famille = "Câble", Designation = $"Câble d'arrivée cuivre {reseau.CableArrivee.Isolant}",
                Calibre = $"{conducteurs}G{N(cable.Section)} mm², {N(reseau.CableArrivee.Longueur)} m" })
            .Ajouter(new ElementMateriel { Repere = "QS0", Famille = "Interrupteur-sectionneur", Designation = "Interrupteur-sectionneur général",
                Calibre = A(qs) }.Avec(qs, CalculDepart.NombrePoles(reseau.Alimentation, reseau.Slt)))
            .Ajouter(new ElementMateriel { Repere = "QG", Famille = "Disjoncteur", Designation = "Disjoncteur général",
                Calibre = $"{A(qg)}, {CalculDepart.Poles(reseau.Alimentation, reseau.Slt)}" }
                .Avec(qg, CalculDepart.NombrePoles(reseau.Alimentation, reseau.Slt), courbe: "C"));
        foreach (var e in arbre.Parcourir()) e.Statut = note.StatutGlobal;
        foreach (var d in departs) tete.Ajouter(d.Materiel);

        // Les appareils de commande sont rattachés à l'alimentation 24 V qui les alimente.
        var alimentation24V = departs.Where(d => d.Depart is DepartAlimentation24V)
            .SelectMany(d => d.Materiel.Parcourir()).FirstOrDefault(e => e.Famille == "Alimentation") ?? tete;
        foreach (var appareil in securite.Appareils)
        {
            appareil.Statut = securite.Note.StatutGlobal;
            alimentation24V.Ajouter(appareil);
        }
        foreach (var appareil in automate.Appareils.SelectMany(a => a.Parcourir()))
            appareil.Statut = automate.Note.StatutGlobal;
        foreach (var appareil in automate.Appareils)
            alimentation24V.Ajouter(appareil);

        var resultatReferences = CatalogueReferences.Appliquer(arbre, projet.References, references ?? CatalogueReferences.Charger());

        var resultat = new ResultatProjet
        {
            Projet = projet, Departs = departs, NoteArrivee = note,
            PuissanceActive = ks * p, PuissanceReactive = ks * q, PuissanceApparente = s, CosPhiGlobal = cosPhi,
            CoefficientSimultaneite = ks, CourantEmploi = ib, CalibreGeneral = qg,
            SectionArrivee = cable.Section, ChuteTensionArrivee = cable.ChuteTension, Arbre = arbre,
            Consommateurs24V = consommateurs24V, Securite = securite, Automate = automate,
            Controles = ControlesConformite.Generer(projet), References = resultatReferences,
        };
        resultat.Selectivite = CalculSelectivite.Calculer(resultat);
        resultat.Armoire = CalculArmoire.Calculer(resultat, projet);
        resultat.Thermique = CalculThermique.Calculer(resultat, resultat.Armoire, projet.Armoire);
        return resultat;
    }

    /// <summary>Consommateurs 24 V DC du projet : ceux déclarés à la main et ceux déduits du matériel.</summary>
    public static IReadOnlyList<ConsommationCommande> Consommateurs24V(Projet projet) =>
        projet.Consommateurs24V
            .Where(c => c.CourantMa > 0 && c.Quantite > 0)
            .Select(c => new ConsommationCommande(c.Quantite > 1 ? $"{c.Designation} × {c.Quantite}" : c.Designation,
                c.CourantMa * c.Quantite / 1000))
            .Concat(CalculSecurite.Consommations(projet.Securite))
            .Concat(CalculAutomate.Consommations(projet.Automate))
            .ToList();

    /// <summary>Coefficient de simultanéité d'une armoire de distribution selon son nombre de circuits.</summary>
    public static double Simultaneite(int departs) => departs switch
    {
        <= 1 => 1,
        <= 3 => 0.9,
        <= 5 => 0.8,
        <= 9 => 0.7,
        _ => 0.6,
    };

    public static string Libelle(TypeAlimentation a) => a switch
    {
        TypeAlimentation.Mono230 => "monophasée 230 V",
        TypeAlimentation.Tri400 => "triphasée 400 V",
        _ => "triphasée 400 V + N",
    };
}
