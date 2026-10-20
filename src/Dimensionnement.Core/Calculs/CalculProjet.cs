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

    /// <summary>Consommateurs de la commande (24 V DC, 24 V AC, 230 V AC) retenus pour dimensionner les sources de commande.</summary>
    public IReadOnlyList<ConsommationCommande> Consommateurs24V { get; init; } = Array.Empty<ConsommationCommande>();
    public double Consommation24V => Consommation(TensionCommande.Continu24);
    public double Consommation(TensionCommande tension) => Consommateurs24V.Where(c => c.Tension == tension).Sum(c => c.Courant);

    /// <summary>Tensions de commande dont le projet a besoin et qu'aucune source ne fournit encore.</summary>
    public IReadOnlyList<BesoinSource> SourcesManquantes { get; init; } = Array.Empty<BesoinSource>();

    /// <summary>Arbre du matériel, de l'arrivée réseau jusqu'à chaque récepteur.</summary>
    public required ElementMateriel Arbre { get; init; }

    public ResultatSecurite? Securite { get; init; }
    public ResultatAutomate? Automate { get; init; }
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

/// <summary>Un poste du bilan de la commande, en A sous sa tension (24 V DC par défaut).</summary>
public record ConsommationCommande(string Designation, double Courant, string Poste = ConsommationCommande.Divers,
    TensionCommande Tension = TensionCommande.Continu24)
{
    public const string Divers = "Divers";
    public const string Automate = "Automate";
    public const string Securite = "Sécurité";
    public const string Actionneurs = "Actionneurs";
}

/// <summary>Calcule tout le projet : les départs, le bilan de puissance et l'arrivée.</summary>
public static class CalculProjet
{
    const string RefBilan = "Guide UTE C 15-105, coefficients d'utilisation et de simultanéité";

    public static ResultatProjet Calculer(Projet projet) => Calculer(projet, CatalogueAutomates.Charger());

    public static ResultatProjet Calculer(Projet projet, IReadOnlyList<CpuAutomate> catalogue,
        IReadOnlyList<ReferenceCatalogue>? references = null, IReadOnlyList<ModeleVariateur>? variateurs = null,
        IReadOnlyList<ModeleEnveloppe>? enveloppes = null, IReadOnlyList<ModeleSecurite>? catalogueSecurite = null,
        IReadOnlyList<CarteEs>? cartes = null, IReadOnlyList<ModeleAlimentation>? alimentations = null, IReadOnlyList<ModeleIhm>? ihm = null,
        IReadOnlyList<ModeleCapteurSecurite>? capteursSecurite = null)
    {
        capteursSecurite ??= CatalogueCapteursSecurite.Charger();
        ihm ??= CatalogueIhm.Charger();
        cartes ??= CatalogueCartesEs.Charger();
        var reseau = projet.Reseau;
        variateurs ??= CatalogueVariateurs.Charger();
        var fabricant = projet.References.Fabricant;

        // Le bilan ne dépend pas des câbles : un premier passage donne les puissances, le second
        // recalcule les départs avec la chute de tension consommée par le câble d'arrivée.
        var consommateurs24V = Consommateurs24V(projet, catalogueSecurite, catalogue, cartes, ihm, capteursSecurite);
        var consommation24V = consommateurs24V.Where(c => c.Tension == TensionCommande.Continu24).Sum(c => c.Courant);
        var repartition = Repartir(projet, consommateurs24V);
        var alimentesPar = SourcesCommande.AlimentesPar(projet);
        ResultatDepart CalculerDepart(Depart d, double chute, ChargeAval? charge) => CalculDepart.Calculer(d, reseau, chute,
            repartition.TryGetValue(d.Id, out var postes) ? postes.Sum(p => p.Courant) : consommation24V, fabricant, variateurs,
            repartition.TryGetValue(d.Id, out var liste) ? liste : null, alimentations, charge);
        // Les transformateurs 400/230 V sont calculés après les départs qu'ils alimentent.
        List<ResultatDepart> Passe(double chute)
        {
            var calcules = new Dictionary<Guid, ResultatDepart>();
            foreach (var d in projet.Departs.Where(d => d is not DepartAlimentation24V { Sortie: TensionCommande.Alternatif230 }))
                calcules[d.Id] = CalculerDepart(d, chute, null);
            foreach (var t in projet.Departs.OfType<DepartAlimentation24V>().Where(t => t.Sortie == TensionCommande.Alternatif230))
            {
                var avals = projet.Departs.Where(d => alimentesPar.TryGetValue(d.Id, out var source) && source == t.Id)
                    .Select(d => calcules[d.Id]).ToList();
                calcules[t.Id] = CalculerDepart(t, chute, new ChargeAval(
                    avals.Sum(r => r.PuissanceActive * r.Depart.CoefficientUtilisation), avals.Sum(r => r.PuissanceReactive * r.Depart.CoefficientUtilisation))
                    { Departs = avals.Select(r => $"départ {r.Depart.Numero} {r.Depart.Designation}".Trim()).ToList() });
            }
            return projet.Departs.Select(d => calcules[d.Id]).ToList();
        }
        var premierPassage = Passe(0);
        // Les départs derrière un transformateur 400/230 V sont comptés au primaire de celui-ci.
        var bilan = premierPassage.Where(r => !alimentesPar.ContainsKey(r.Depart.Id)).ToList();

        var note = new NoteDeCalcul("ARR", projet.ForcagesArrivee);
        var p = bilan.Sum(r => r.PuissanceActive * r.Depart.CoefficientUtilisation);
        var q = bilan.Sum(r => r.PuissanceReactive * r.Depart.CoefficientUtilisation);
        note.Info("Puissances utilisées", $"Σ (P × ku) = {N(p)} kW ; Σ (Q × ku) = {N(q)} kvar", RefBilan);
        if (alimentesPar.Count > 0)
            note.Info("Départs derrière un transformateur", $"{alimentesPar.Count} départ(s) 230 V comptés au primaire de leur transformateur 400/230 V.",
                RefBilan);

        var ksTable = Simultaneite(bilan.Count);
        var ks = note.Valeur(CodesCalcul.Simultaneite, "Coefficient de simultanéité", ksTable,
            ksTable, "",
            "ks selon le nombre de départs", $"{bilan.Count} départ(s)", RefBilan);

        var s = ks * Math.Sqrt(p * p + q * q);
        var cosPhi = s > 0 ? ks * p / s : 1;
        var mono = reseau.Alimentation == TypeAlimentation.Mono230;
        var ib = s * 1000 / (mono ? 230 : Math.Sqrt(3) * 400);
        note.Valeur(CodesCalcul.CourantEmploi, "Courant d'emploi de l'armoire", ib, ib, "A",
            mono ? "Ib = S / U" : "Ib = S / (√3 × U)",
            $"S = ks × √(P² + Q²) = {N(s)} kVA, cos φ global = {N(cosPhi)}", RefBilan);
        if (projet.Departs.Any(d => d.Alimentation is TypeAlimentation.Mono230 or TypeAlimentation.Bi400) && !mono)
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

        var departs = Passe(cable.ChuteTension);
        if (consommation24V > 0)
            note.Info("Bilan 24 V DC", $"Consommation des circuits de commande : {A(consommation24V)}.", "Bilan 24 V DC du projet");
        var manquantes = SourcesCommande.Manquantes(projet, consommateurs24V);
        foreach (var b in manquantes)
            note.Verifier($"Source {SourcesCommande.Libelle(b.Tension)}", false, b.Raison, "NF EN 60204-1, 7.2.7 et 9.1.1", StatutCalcul.Alerte);
        var securite = CalculSecurite.Calculer(projet, departs, catalogueSecurite, capteursSecurite);
        var automate = CalculAutomate.Calculer(projet.Automate, catalogue, cartes, ihm);

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
        ElementMateriel? Source(Guid id) => departs.FirstOrDefault(r => r.Depart.Id == id)?.Materiel.Parcourir()
            .FirstOrDefault(e => e.Famille == "Alimentation");
        foreach (var d in departs)
            (alimentesPar.TryGetValue(d.Depart.Id, out var source) ? Source(source) ?? tete : tete).Ajouter(d.Materiel);
        MarquerBobines(arbre, projet.TensionBobines);

        // Les appareils de commande sont rattachés à l'alimentation 24 V DC qui les alimente.
        ElementMateriel Alimentation(string poste) =>
            departs.Where(d => d.Depart is DepartAlimentation24V { Sortie: TensionCommande.Continu24 } && repartition.TryGetValue(d.Depart.Id, out var l) && l.Any(p => p.Poste == poste))
                .Concat(departs.Where(d => d.Depart is DepartAlimentation24V { Sortie: TensionCommande.Continu24 }))
                .SelectMany(d => d.Materiel.Parcourir()).FirstOrDefault(e => e.Famille == "Alimentation") ?? tete;
        foreach (var appareil in securite.Appareils)
        {
            appareil.Statut = securite.Note.StatutGlobal;
            Alimentation(ConsommationCommande.Securite).Ajouter(appareil);
        }
        foreach (var appareil in automate.Appareils.SelectMany(a => a.Parcourir()))
            appareil.Statut = automate.Note.StatutGlobal;
        foreach (var appareil in automate.Appareils)
            Alimentation(ConsommationCommande.Automate).Ajouter(appareil);

        var resultatReferences = CatalogueReferences.Appliquer(arbre, projet.References, references ?? CatalogueReferences.Charger());
        foreach (var km in arbre.Parcourir().Where(e => e.Famille == "Contacteur"))
        {
            km.Reference = CatalogueReferences.Bobine(km.Reference, projet.TensionBobines);
            km.Fabricant = km.Fabricant.Replace("bobine 24 V DC", $"bobine {SourcesCommande.Libelle(projet.TensionBobines)}");
            if (projet.TensionBobines == TensionCommande.Alternatif230)
                km.Fabricant = km.Fabricant.Replace("24-60 V AC/DC", "100-250 V AC/DC");
        }

        var resultat = new ResultatProjet
        {
            Projet = projet, Departs = departs, NoteArrivee = note,
            PuissanceActive = ks * p, PuissanceReactive = ks * q, PuissanceApparente = s, CosPhiGlobal = cosPhi,
            CoefficientSimultaneite = ks, CourantEmploi = ib, CalibreGeneral = qg,
            SectionArrivee = cable.Section, ChuteTensionArrivee = cable.ChuteTension, Arbre = arbre,
            Consommateurs24V = consommateurs24V, SourcesManquantes = manquantes, Securite = securite, Automate = automate,
            References = resultatReferences,
        };
        resultat.Selectivite = CalculSelectivite.Calculer(resultat);
        resultat.Armoire = CalculArmoire.Calculer(resultat, projet, enveloppes);
        resultat.Thermique = CalculThermique.Calculer(resultat, resultat.Armoire, projet.Armoire);
        return resultat;
    }

    /// <summary>Consommateurs 24 V DC du projet : ceux déclarés à la main et ceux déduits du matériel.</summary>
    public static IReadOnlyList<ConsommationCommande> Consommateurs24V(Projet projet, IReadOnlyList<ModeleSecurite>? securite = null,
        IReadOnlyList<CpuAutomate>? cpus = null, IReadOnlyList<CarteEs>? cartes = null, IReadOnlyList<ModeleIhm>? ihm = null,
        IReadOnlyList<ModeleCapteurSecurite>? capteurs = null) =>
        projet.Consommateurs24V
            .Where(c => c.CourantMa > 0 && c.Quantite > 0)
            .Select(c => new ConsommationCommande(c.Quantite > 1 ? $"{c.Designation} × {c.Quantite}" : c.Designation,
                c.CourantMa * c.Quantite / 1000, Tension: c.Tension))
            .Concat(CalculSecurite.Consommations(projet, securite, capteurs).Select(c => c with { Poste = ConsommationCommande.Securite }))
            .Concat(CalculAutomate.Consommations(projet.Automate, cpus, cartes, ihm).Select(c => c with { Poste = ConsommationCommande.Automate }))
            .Concat(projet.EstimerPeripherie24V ? Peripherie24V(projet) : Enumerable.Empty<ConsommationCommande>())
            .ToList();

    /// <summary>Courant d'une bobine de contacteur 24 V DC basse consommation (2,4 W), en A.</summary>
    public const double BobineContacteur = 0.1;
    /// <summary>Puissance de maintien d'une bobine de contacteur alternative, en VA (l'appel vaut 6 à 10 fois plus).</summary>
    public const double MaintienBobineAc = 7;

    /// <summary>Courant de maintien d'une bobine sous sa tension, en A.</summary>
    public static double CourantBobine(TensionCommande t) => t switch
    {
        TensionCommande.Continu24 => BobineContacteur,
        TensionCommande.Alternatif24 => MaintienBobineAc / 24,
        _ => MaintienBobineAc / 230,
    };

    /// <summary>Indique la tension de bobine sur chaque contacteur de l'arbre.</summary>
    public static void MarquerBobines(ElementMateriel arbre, TensionCommande tension)
    {
        foreach (var km in arbre.Parcourir().Where(e => e.Famille == "Contacteur" && !e.Designation.Contains("bobine")))
            km.Designation += $", bobine {SourcesCommande.Libelle(tension)}";
    }
    /// <summary>Actionneur sur sortie TOR (électrovanne, voyant), en A, et part en service simultanément.</summary>
    public const double Actionneur = 0.1;
    public const double SimultaneiteActionneurs = 0.5;
    /// <summary>Capteur 3 fils sur entrée TOR et boucle 4-20 mA sur entrée analogique, en A.</summary>
    public const double CapteurTor = 0.015;
    public const double CapteurAnalogique = 0.025;

    /// <summary>Contacteurs commandés en 24 V DC : ligne, inverseur, étoile-triangle, et contacteurs redondants de sécurité.</summary>
    public static int Contacteurs(Projet projet)
    {
        var redondants = projet.Securite.Fonctions.Where(f => CalculSecurite.Redondant(f.PLr)).SelectMany(f => f.Departs).Distinct()
            .Count(n => projet.Departs.Any(d => d.Numero == n && d is DepartMoteur { Demarrage: not ModeDemarrage.Variateur }));
        return redondants + projet.Departs.OfType<DepartMoteur>().Sum(m => m.Demarrage switch
        {
            ModeDemarrage.Inverseur => 2,
            ModeDemarrage.EtoileTriangle => 3,
            ModeDemarrage.Variateur or ModeDemarrage.DemarreurProgressif => 0,
            _ => 1,
        });
    }

    /// <summary>Périphérie 24 V estimée : bobines de contacteurs, capteurs et actionneurs des E/S de l'automate et des stations de l'armoire.</summary>
    public static IEnumerable<ConsommationCommande> Peripherie24V(Projet projet)
    {
        var contacteurs = Contacteurs(projet);
        if (contacteurs > 0)
        {
            var t = projet.TensionBobines;
            var puissance = t == TensionCommande.Continu24 ? "2,4 W" : $"{MaintienBobineAc:0} VA de maintien";
            yield return new($"Bobines de contacteurs × {contacteurs} ({SourcesCommande.Libelle(t)}, {puissance}, estimation)",
                contacteurs * CourantBobine(t), ConsommationCommande.Actionneurs, t);
        }
        var a = projet.Automate;
        if (!a.Actif) yield break;
        var stations = a.Stations.Where(s => s.DansArmoire).ToList();
        var di = a.EntreesTor + stations.Sum(s => s.EntreesTor);
        var dq = a.SortiesTor + stations.Sum(s => s.SortiesTor);
        var ai = a.EntreesAnalogiques + stations.Sum(s => s.EntreesAnalogiques);
        if (di > 0)
            yield return new($"Capteurs sur entrées TOR × {di} ({CapteurTor * 1000:0} mA, estimation)", di * CapteurTor, ConsommationCommande.Automate);
        if (ai > 0)
            yield return new($"Boucles 4-20 mA × {ai} (estimation)", ai * CapteurAnalogique, ConsommationCommande.Automate);
        if (dq > 0)
            yield return new($"Actionneurs sur sorties TOR × {dq} ({Actionneur * 1000:0} mA, {SimultaneiteActionneurs * 100:0} % simultanés, estimation)",
                dq * Actionneur * SimultaneiteActionneurs, ConsommationCommande.Actionneurs);
    }

    /// <summary>
    /// Répartit les consommateurs sur les alimentations 24 V : chaque partie va aux alimentations qui lui sont dédiées,
    /// sinon à celles de toute la commande, sinon à la première ; plusieurs alimentations retenues se partagent la charge.
    /// </summary>
    public static Dictionary<Guid, List<ConsommationCommande>> Repartir(Projet projet, IReadOnlyList<ConsommationCommande> consommateurs)
    {
        var toutes = projet.Departs.OfType<DepartAlimentation24V>().ToList();
        var repartition = toutes.ToDictionary(a => a.Id, _ => new List<ConsommationCommande>());
        if (toutes.Count == 0) return repartition;
        static UsageAlimentation24V Usage(string poste) => poste switch
        {
            ConsommationCommande.Automate => UsageAlimentation24V.Automate,
            ConsommationCommande.Securite => UsageAlimentation24V.Securite,
            ConsommationCommande.Actionneurs => UsageAlimentation24V.Actionneurs,
            _ => UsageAlimentation24V.Commande,
        };
        foreach (var c in consommateurs)
        {
            // Un consommateur ne va qu'aux sources de sa tension.
            var alimentations = toutes.Where(a => a.Sortie == c.Tension).ToList();
            if (alimentations.Count == 0) continue;
            var usage = Usage(c.Poste);
            var cibles = alimentations.Where(a => a.Usage == usage).ToList();
            if (cibles.Count == 0) cibles = alimentations.Where(a => a.Usage == UsageAlimentation24V.Commande).ToList();
            if (cibles.Count == 0) cibles = alimentations.Take(1).ToList();
            foreach (var a in cibles)
                repartition[a.Id].Add(cibles.Count == 1 ? c : c with { Designation = $"{c.Designation} (1/{cibles.Count})", Courant = c.Courant / cibles.Count });
        }
        return repartition;
    }

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
        TypeAlimentation.Bi400 => "biphasée 400 V",
        TypeAlimentation.Tri400 => "triphasée 400 V",
        _ => "triphasée 400 V + N",
    };
}
