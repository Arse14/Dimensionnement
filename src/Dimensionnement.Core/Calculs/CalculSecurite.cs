using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public record ResultatFonctionSecurite(FonctionSecurite Fonction, string Categorie, string Architecture);

/// <summary>Taux de remplissage d'un appareil de sécurité : voies d'entrée et sorties sûres utilisées et disponibles.</summary>
public record CapaciteSecurite(string Repere, string Reference, string Usage, int EntreesUtilisees, int EntreesDisponibles,
    int SortiesUtilisees, int SortiesDisponibles)
{
    public bool Depassee => EntreesUtilisees > EntreesDisponibles || SortiesUtilisees > SortiesDisponibles;
    public string Entrees => EntreesDisponibles > 0 || EntreesUtilisees > 0 ? $"{EntreesUtilisees} / {EntreesDisponibles}" : "–";
    public string Sorties => $"{SortiesUtilisees} / {SortiesDisponibles}";
}

public class ResultatSecurite
{
    public required NoteDeCalcul Note { get; init; }
    public required IReadOnlyList<ResultatFonctionSecurite> Fonctions { get; init; }

    /// <summary>Appareils de sécurité (relais, extensions ou automate et modules) à raccorder au 24 V.</summary>
    public required IReadOnlyList<ElementMateriel> Appareils { get; init; }
    public required IReadOnlyList<ConsommationCommande> Consommations { get; init; }
    public IReadOnlyList<CapaciteSecurite> Capacites { get; init; } = Array.Empty<CapaciteSecurite>();
}

/// <summary>Logique de sécurité retenue au catalogue, avec les raisons des changements de référence.</summary>
public record SelectionSecurite(bool Automate, List<ElementMateriel> Appareils, List<CapaciteSecurite> Capacites,
    List<(string Libelle, string Message, StatutCalcul Statut)> Messages, List<ConsommationCommande> Consommations);

/// <summary>
/// Préconisations de sécurité machine selon la NF EN ISO 13849-1 : catégorie d'architecture, logique de sécurité
/// (relais ou automate) choisie au catalogue selon ses capacités de câblage, et partie puissance.
/// Le PL atteint reste à calculer et valider dans SISTEMA.
/// </summary>
public static class CalculSecurite
{
    const string Ref = "NF EN ISO 13849-1";
    const string RefCatalogue = "Catalogues Pilz, Schmersal, Siemens, Schneider (indicatifs, à vérifier)";

    /// <summary>Au-delà de ce nombre de fonctions, un automate de sécurité configurable est préconisé.</summary>
    public const int SeuilAutomate = 3;

    /// <summary>Extensions de contacts admises derrière un relais avant de passer à l'automate.</summary>
    const int ExtensionsRelaisMax = 3;

    /// <summary>Catégorie d'architecture conseillée pour atteindre un PLr.</summary>
    public static string Categorie(NiveauPerformance plr) => plr switch
    {
        NiveauPerformance.a => "B",
        NiveauPerformance.b => "1",
        NiveauPerformance.c => "2",
        NiveauPerformance.d => "3",
        _ => "4",
    };

    /// <summary>Vrai si la catégorie impose une partie puissance redondante (deux canaux).</summary>
    public static bool Redondant(NiveauPerformance plr) => plr >= NiveauPerformance.d;

    /// <summary>Sorties sûres nécessaires : une par contacteur ou voie STO coupé (deux en redondance), plus les sorties ajoutées.</summary>
    public static int SortiesNecessaires(FonctionSecurite f, Projet projet) =>
        Math.Max(1, f.Departs.Count(n => projet.Departs.Any(d => d.Numero == n)) * (Redondant(f.PLr) ? 2 : 1) + Math.Max(0, f.SortiesSupplementaires));

    /// <summary>
    /// Voies d'entrée d'automate : deux par capteur à partir de PL c, quatre par commande bimanuelle,
    /// toujours deux (une paire OSSD) par barrière immatérielle ou scrutateur.
    /// </summary>
    public static int EntreesNecessaires(FonctionSecurite f) =>
        Math.Max(1, f.NombreCapteurs) * (f.PLr >= NiveauPerformance.c || f.CapteurOptique ? 2 : 1) * (f.Type == TypeFonctionSecurite.CommandeBimanuelle ? 2 : 1);

    /// <summary>
    /// Capteurs à raccorder sur un relais : jusqu'à PL c, plusieurs capteurs peuvent être câblés en série sur une entrée ;
    /// au-delà, le masquage de défauts (ISO/TR 24119) impose une entrée par capteur.
    /// </summary>
    public static int CapteursRelais(FonctionSecurite f) => f.PLr <= NiveauPerformance.c ? 1 : Math.Max(1, f.NombreCapteurs);

    public static ResultatSecurite Calculer(Projet projet, IReadOnlyList<ResultatDepart> departs, IReadOnlyList<ModeleSecurite>? catalogue = null,
        IReadOnlyList<ModeleCapteurSecurite>? capteurs = null)
    {
        var config = projet.Securite;
        var note = new NoteDeCalcul("SEC");
        var fonctions = new List<ResultatFonctionSecurite>();
        if (config.Fonctions.Count == 0)
            return new ResultatSecurite { Note = note, Fonctions = fonctions, Appareils = new List<ElementMateriel>(), Consommations = new List<ConsommationCommande>() };

        var selection = Selectionner(projet, catalogue ?? CatalogueSecurite.Charger(), capteurs);
        note.Info("Logique de sécurité", selection.Automate
            ? $"{config.Fonctions.Count} fonction(s) : automate de sécurité {selection.Appareils[0].Fabricant}, {selection.Appareils[0].Reference}" +
              (selection.Appareils.Count > 1 ? $" + {selection.Appareils.Count - 1} module(s)." : ".")
            : $"{config.Fonctions.Count} fonction(s) : un relais de sécurité par fonction ({FamilleRelais(config.Fabricant)}).", Ref);
        foreach (var (libelle, message, statut) in selection.Messages)
        {
            if (statut == StatutCalcul.Info) note.Info(libelle, message, RefCatalogue);
            else note.Verifier(libelle, statut == StatutCalcul.Conforme, message, RefCatalogue, statut);
        }
        foreach (var c in selection.Capacites)
            note.Verifier($"Capacité {c.Repere}", !c.Depassee,
                $"{c.Reference} ({c.Usage}) : entrées {c.Entrees}, sorties de sécurité {c.Sorties}.", RefCatalogue);

        foreach (var f in config.Fonctions)
        {
            var categorie = Categorie(f.PLr);
            var architecture = Redondant(f.PLr)
                ? "deux canaux, surveillance des contacts miroirs, détection des défauts"
                : f.PLr == NiveauPerformance.c ? "un canal avec fonction de test" : "un canal, composants éprouvés";
            note.Info($"{f.Nom} (PLr {f.PLr})", $"Catégorie {categorie} conseillée : {architecture}. " +
                $"{f.NombreCapteurs} capteur(s), {SortiesNecessaires(f, projet)} sortie(s) de sécurité ; {Capteurs(f.Type)}", Ref);
            fonctions.Add(new ResultatFonctionSecurite(f, categorie, architecture));

            foreach (var numero in f.Departs)
            {
                var depart = departs.FirstOrDefault(d => d.Depart.Numero == numero);
                if (depart is null)
                {
                    note.Verifier($"{f.Nom} : départ D{numero:00}", false, "Ce départ n'existe pas dans le projet.",
                        siEchec: StatutCalcul.Alerte);
                    continue;
                }
                PartiePuissance(f, depart, note);
            }
        }

        note.Info("Validation", "Calculer le PL atteint (MTTFd, DCavg, CCF) dans SISTEMA à partir de l'export des fonctions, " +
                                "puis le comparer au PLr de chaque fonction.", Ref);
        return new ResultatSecurite
        {
            Note = note, Fonctions = fonctions, Appareils = selection.Appareils, Consommations = selection.Consommations,
            Capacites = selection.Capacites,
        };
    }

    /// <summary>
    /// Choisit au catalogue un relais par fonction, avec des extensions de contacts si ses sorties ne suffisent pas,
    /// ou un automate de sécurité et ses modules quand les relais ne conviennent plus (nombre de fonctions, de capteurs ou de sorties).
    /// </summary>
    public static SelectionSecurite Selectionner(Projet projet, IReadOnlyList<ModeleSecurite> catalogue,
        IReadOnlyList<ModeleCapteurSecurite>? capteurs = null)
    {
        var selection = SelectionnerLogique(projet, catalogue);
        AjouterCapteursOptiques(projet, capteurs ?? CatalogueCapteursSecurite.Charger(), selection);
        return selection;
    }

    static SelectionSecurite SelectionnerLogique(Projet projet, IReadOnlyList<ModeleSecurite> catalogue)
    {
        var config = projet.Securite;
        var appareils = new List<ElementMateriel>();
        var capacites = new List<CapaciteSecurite>();
        var messages = new List<(string, string, StatutCalcul)>();
        var consommations = new List<ConsommationCommande>();
        if (config.Fonctions.Count == 0) return new SelectionSecurite(false, appareils, capacites, messages, consommations);

        var relais = config.Fonctions.Select(f => (Fonction: f, Choix: ChoisirRelais(f, projet, config.Fabricant, catalogue))).ToList();
        var automate = config.ImposerAutomate || config.Fonctions.Count >= SeuilAutomate || relais.Any(r => r.Choix is null);
        if (!config.ImposerAutomate && config.Fonctions.Count < SeuilAutomate)
            foreach (var (f, _) in relais.Where(r => r.Choix is null))
                messages.Add(($"Changement de logique : {f.Nom}",
                    $"Aucun relais {config.Fabricant} ne reçoit {CapteursRelais(f)} capteur(s) et {SortiesNecessaires(f, projet)} sortie(s) " +
                    $"pour cette fonction : passage à un automate de sécurité.", StatutCalcul.Info));

        if (!automate)
        {
            var i = 0;
            foreach (var (f, choix) in relais)
            {
                i++;
                var (modele, extension, nombre, minimal) = choix!.Value;
                var sorties = SortiesNecessaires(f, projet);
                var element = Element($"KA{i}", $"Relais de sécurité – {f.Nom}", modele,
                    $"{modele.Capteurs} capteur(s), {modele.Sorties} contacts de sécurité, cat. {Categorie(f.PLr)}");
                appareils.Add(element);
                consommations.Add(new ConsommationCommande($"Relais de sécurité {f.Nom} ({modele.Reference})", modele.Consommation));
                capacites.Add(new CapaciteSecurite(element.Repere, modele.Reference, f.Nom, CapteursRelais(f), modele.Capteurs,
                    sorties, modele.Sorties + nombre * (extension?.Sorties ?? 0)));
                for (var k = 1; k <= nombre; k++)
                {
                    appareils.Add(Element($"KA{i}.{k}", $"Extension de contacts – {f.Nom}", extension!, $"{extension!.Sorties} contacts de sécurité"));
                    consommations.Add(new ConsommationCommande($"Extension de contacts {f.Nom}", extension.Consommation));
                }
                if (modele != minimal || nombre > 0)
                    messages.Add(($"Référence ajustée : {f.Nom}",
                        $"{sorties} sortie(s) de sécurité nécessaires, {minimal.Sorties} sur le {minimal.Reference} : " +
                        $"{modele.Reference}{(nombre > 0 ? $" + {nombre} × {extension!.Reference}" : "")}.", StatutCalcul.Info));
            }
            return new SelectionSecurite(false, appareils, capacites, messages, consommations);
        }

        // Automate : toutes les fonctions sur un seul appareil configurable, complété de modules d'E/S.
        var entrees = config.Fonctions.Sum(EntreesNecessaires);
        var sortiesTotal = config.Fonctions.Sum(f => SortiesNecessaires(f, projet));
        var choixAutomate = ChoisirAutomate(config.Fabricant, entrees, sortiesTotal, catalogue);
        if (choixAutomate is null)
        {
            messages.Add(("Automate de sécurité", $"Aucun automate {config.Fabricant} dans le catalogue.", StatutCalcul.NonConforme));
            return new SelectionSecurite(true, appareils, capacites, messages, consommations);
        }
        var (baseAutomate, modules) = choixAutomate.Value;
        appareils.Add(Element("KA-SEC", "Automate de sécurité", baseAutomate, $"{baseAutomate.Entrees} entrées, {baseAutomate.Sorties} sorties sûres"));
        consommations.Add(new ConsommationCommande($"Automate de sécurité ({baseAutomate.Reference})", baseAutomate.Consommation));
        var k2 = 0;
        foreach (var m in modules)
        {
            k2++;
            appareils.Add(Element($"KA-SEC.{k2}", "Module d'E/S de sécurité", m, $"{m.Entrees} entrées, {m.Sorties} sorties sûres"));
            consommations.Add(new ConsommationCommande($"Module de sécurité {m.Reference}", m.Consommation));
        }
        var entreesDispo = baseAutomate.Entrees + modules.Sum(m => m.Entrees);
        var sortiesDispo = baseAutomate.Sorties + modules.Sum(m => m.Sorties);
        capacites.Add(new CapaciteSecurite("KA-SEC", baseAutomate.Reference + (modules.Count > 0 ? $" + {modules.Count} module(s)" : ""),
            "toutes les fonctions", entrees, entreesDispo, sortiesTotal, sortiesDispo));
        if (modules.Count > baseAutomate.ExtensionsMax)
            messages.Add(("Extensions de l'automate", $"{modules.Count} modules pour {baseAutomate.ExtensionsMax} admis au plus.", StatutCalcul.NonConforme));
        else if (modules.Count > 0)
            messages.Add(("Référence ajustée : automate", $"{entrees} voies d'entrée et {sortiesTotal} sorties de sécurité nécessaires : " +
                $"{baseAutomate.Reference} + {string.Join(", ", modules.GroupBy(m => m.Reference).Select(g => $"{g.Count()} × {g.Key}"))}.", StatutCalcul.Info));
        return new SelectionSecurite(true, appareils, capacites, messages, consommations);
    }

    /// <summary>Plus petit relais de la marque qui traite la fonction, complété d'extensions de contacts si besoin.</summary>
    static (ModeleSecurite Modele, ModeleSecurite? Extension, int Nombre, ModeleSecurite Minimal)? ChoisirRelais(
        FonctionSecurite f, Projet projet, FabricantSecurite fabricant, IReadOnlyList<ModeleSecurite> catalogue)
    {
        var candidats = CatalogueSecurite.De(fabricant, "Relais", catalogue)
            .Where(m => m.Traite(f.Type) && m.Capteurs >= CapteursRelais(f))
            .OrderBy(m => m.Sorties).ThenBy(m => m.Largeur).ToList();
        if (candidats.Count == 0) return null;
        var sorties = SortiesNecessaires(f, projet);
        if (candidats.FirstOrDefault(m => m.Sorties >= sorties) is { } direct) return (direct, null, 0, candidats[0]);
        var extension = CatalogueSecurite.De(fabricant, "Extension contacts", catalogue).OrderByDescending(m => m.Sorties).FirstOrDefault();
        if (extension is not { Sorties: > 0 }) return null;
        var plusGrand = candidats[^1];
        var nombre = (int)Math.Ceiling((sorties - plusGrand.Sorties) / (double)extension.Sorties);
        return nombre <= ExtensionsRelaisMax ? (plusGrand, extension, nombre, candidats[0]) : null;
    }

    /// <summary>Plus petit automate de la marque qui couvre les E/S seul, sinon le premier qui les couvre avec ses modules.</summary>
    static (ModeleSecurite Base, List<ModeleSecurite> Modules)? ChoisirAutomate(FabricantSecurite fabricant, int entrees, int sorties,
        IReadOnlyList<ModeleSecurite> catalogue)
    {
        var bases = CatalogueSecurite.De(fabricant, "Automate", catalogue).OrderBy(b => b.Entrees + b.Sorties).ThenBy(b => b.Largeur).ToList();
        if (bases.Count == 0) return null;
        if (bases.FirstOrDefault(b => b.Entrees >= entrees && b.Sorties >= sorties && (b.Entrees + b.Sorties) > 0) is { } seul)
            return (seul, new List<ModeleSecurite>());

        var modules = CatalogueSecurite.De(fabricant, "Module E/S", catalogue).ToList();
        var moduleEntrees = modules.Where(m => m.Entrees > 0).OrderBy(m => m.Sorties).ThenByDescending(m => m.Entrees).FirstOrDefault();
        var moduleSorties = modules.Where(m => m.Sorties > 0).OrderBy(m => m.Entrees).ThenByDescending(m => m.Sorties).FirstOrDefault();
        (ModeleSecurite, List<ModeleSecurite>)? dernier = null;
        foreach (var b in bases.Where(b => b.ExtensionsMax > 0))
        {
            var liste = new List<ModeleSecurite>();
            var manqueSorties = Math.Max(0, sorties - b.Sorties);
            if (manqueSorties > 0 && moduleSorties is not null)
                liste.AddRange(Enumerable.Repeat(moduleSorties, (int)Math.Ceiling(manqueSorties / (double)moduleSorties.Sorties)));
            var manqueEntrees = Math.Max(0, entrees - b.Entrees - liste.Sum(m => m.Entrees));
            if (manqueEntrees > 0 && moduleEntrees is not null)
                liste.AddRange(Enumerable.Repeat(moduleEntrees, (int)Math.Ceiling(manqueEntrees / (double)moduleEntrees.Entrees)));
            dernier = (b, liste);
            if (liste.Count <= b.ExtensionsMax) return dernier;
        }
        return dernier ?? (bases[^1], new List<ModeleSecurite>());
    }

    static ElementMateriel Element(string repere, string designation, ModeleSecurite m, string calibre) => new()
    {
        Repere = repere, Famille = "Sécurité", Designation = designation, Calibre = calibre,
        Fabricant = $"{m.Fabricant} – {m.Gamme}", Reference = m.Reference,
        LargeurMm = m.Largeur, HauteurMm = 100, ProfondeurMm = 115, PertesW = Math.Max(1, 24 * m.Consommation),
    };

    public static bool AvecAutomate(Projet projet, IReadOnlyList<ModeleSecurite>? catalogue = null) =>
        SelectionnerLogique(projet, catalogue ?? CatalogueSecurite.Charger()).Automate;

    /// <summary>Consommation 24 V de la logique de sécurité retenue et des barrières et scrutateurs.</summary>
    public static IEnumerable<ConsommationCommande> Consommations(Projet projet, IReadOnlyList<ModeleSecurite>? catalogue = null,
        IReadOnlyList<ModeleCapteurSecurite>? capteurs = null) =>
        Selectionner(projet, catalogue ?? CatalogueSecurite.Charger(), capteurs).Consommations;

    /// <summary>Repère de la protection 24 V DC des barrières et scrutateurs.</summary>
    public const string ReperePotectionCapteurs = "F-SEC";

    /// <summary>
    /// Barrières immatérielles et scrutateurs laser choisis au catalogue pour chaque fonction qui en utilise.
    /// Montés sur la machine (hors plaque), ils sont rattachés à une protection 24 V DC dédiée dans l'armoire,
    /// dimensionnée sur leur consommation ; leurs câbles et bornes sont repris au devis.
    /// </summary>
    static void AjouterCapteursOptiques(Projet projet, IReadOnlyList<ModeleCapteurSecurite> catalogue, SelectionSecurite selection)
    {
        var optiques = projet.Securite.Fonctions.Where(f => f.CapteurOptique).ToList();
        if (optiques.Count == 0) return;
        var appareils = new List<ElementMateriel>();
        var total = 0.0;
        var barrieres = 0;
        var scrutateurs = 0;
        foreach (var f in optiques)
        {
            var nombre = Math.Max(1, f.NombreCapteurs);
            var nature = f.Type == TypeFonctionSecurite.ScrutateurLaser ? "scrutateur laser" : "barrière immatérielle";
            if (CatalogueCapteursSecurite.Choisir(f, catalogue) is not var (m, repli))
            {
                selection.Messages.Add(($"Capteur : {f.Nom}", f.Type == TypeFonctionSecurite.ScrutateurLaser
                    ? $"Aucun scrutateur du catalogue n'a un champ de protection de {N(f.Portee)} m : compléter le catalogue des capteurs."
                    : $"Aucune barrière du catalogue ne couvre type {CatalogueCapteursSecurite.TypeMinimal(f.PLr)}, détection {Detection(f.Detection)}, " +
                      $"{N(f.HauteurProtegee)} mm et {N(f.Portee)} m : compléter le catalogue des capteurs.", StatutCalcul.NonConforme));
                continue;
            }
            var hauteur = CatalogueCapteursSecurite.HauteurRetenue(m, f.HauteurProtegee);
            var calibre = m.EstBarriere
                ? (m.MultiFaisceaux
                    ? $"type {m.TypeIec}, {m.Faisceaux} faisceaux espacés de {N(m.Resolution)} mm, hauteur {N(hauteur)} mm, portée {N(m.Portee)} m"
                    : $"type {m.TypeIec}, résolution {N(m.Resolution)} mm, hauteur protégée {N(hauteur)} mm, portée {N(m.Portee)} m")
                : $"type {m.TypeIec}, champ de protection {N(m.Portee)} m, {N(m.Angle)}°, {m.Ossd} paire(s) OSSD";
            for (var k = 1; k <= nombre; k++)
            {
                var repere = m.EstBarriere ? $"BI{++barrieres}" : $"BL{++scrutateurs}";
                appareils.Add(new ElementMateriel
                {
                    Repere = repere, Famille = "Capteur de sécurité", Designation = $"{(m.EstBarriere ? "Barrière immatérielle" : "Scrutateur laser")} – {f.Nom}",
                    Calibre = calibre, Fabricant = $"{m.Fabricant} – {m.Gamme}", Reference = m.Reference, HorsArmoire = true,
                    PertesW = 24 * m.Consommation,
                });
            }
            total += nombre * m.Consommation;
            selection.Consommations.Add(new ConsommationCommande($"{(m.EstBarriere ? "Barrière immatérielle" : "Scrutateur laser")} {f.Nom}" +
                (nombre > 1 ? $" × {nombre}" : ""), nombre * m.Consommation));
            selection.Messages.Add(($"Capteur : {f.Nom}", $"{nombre} × {nature} {m.Fabricant} {m.Gamme}, {calibre}" +
                (m.Reference.Length > 0 ? $" ({m.Reference})" : " ; référence selon la hauteur, à relever au configurateur") +
                (repli ? $". {f.FabricantCapteur} n'en propose pas au catalogue : autre marque retenue." : "."), StatutCalcul.Info));
            if (m.EstBarriere && !m.MultiFaisceaux && hauteur < f.HauteurProtegee)
                selection.Messages.Add(($"Hauteur protégée : {f.Nom}", $"{N(f.HauteurProtegee)} mm demandés, {N(hauteur)} mm au plus dans la gamme " +
                    "retenue : prévoir deux barrières en cascade.", StatutCalcul.Alerte));
            // Un scrutateur type 3 ne dépasse pas PL d (NF EN IEC 61496-3).
            if (!m.EstBarriere && f.PLr > NiveauPerformance.d)
                selection.Messages.Add(($"PL du scrutateur : {f.Nom}", $"Un scrutateur type {m.TypeIec} atteint au plus PL {m.Pl} : " +
                    $"PLr {f.PLr} impossible, prévoir une barrière type 4 ou réduire le risque.", StatutCalcul.NonConforme));
            selection.Messages.Add(($"Distance de sécurité : {f.Nom}", "Calculer la distance minimale au danger S = K × T + C (NF EN ISO 13855) " +
                "avec le temps de réponse du capteur et le temps d'arrêt de la machine.", StatutCalcul.Info));
        }
        if (appareils.Count == 0) return;

        // Protection dédiée : un défaut sur un câble de capteur ne doit pas couper le 24 V de l'automate.
        var calibreProtection = Calibres.Superieur(Calibres.Disjoncteurs, Math.Max(1, 1.25 * total)) ?? Calibres.Disjoncteurs[^1];
        var protection = new ElementMateriel
        {
            Repere = ReperePotectionCapteurs, Famille = "Disjoncteur", Designation = "Protection 24 V DC des barrières et scrutateurs",
            Calibre = $"{N(calibreProtection)} A, 1P, courbe C, 24 V DC",
        }.Avec(calibreProtection, 1, courbe: "C");
        protection.Enfants.AddRange(appareils);
        selection.Appareils.Add(protection);
        selection.Messages.Add(("Protection des capteurs", $"{ReperePotectionCapteurs} : {N(calibreProtection)} A courbe C pour {A(total)} consommés " +
            "par les barrières et scrutateurs (marge 25 %), hors charge des sorties OSSD.", StatutCalcul.Info));
    }

    static string Detection(DetectionBarriere d) => d switch
    {
        DetectionBarriere.Doigt => "doigt (14 mm)",
        DetectionBarriere.Main => "main (30 mm)",
        _ => "corps (multi-faisceaux)",
    };

    /// <summary>Ajoute à un départ les appareils de coupure qu'impose la fonction de sécurité.</summary>
    static void PartiePuissance(FonctionSecurite f, ResultatDepart depart, NoteDeCalcul note)
    {
        var n = depart.Depart.Numero;
        var variateur = depart.Depart is DepartMoteur { SurVariateur: true };
        if (variateur)
        {
            depart.Note.Info($"Sécurité : {f.Nom}", $"Entrée STO du variateur câblée sur la logique de sécurité ; " +
                $"le PL de la fonction STO du variateur doit être ≥ {f.PLr}.", "NF EN 61800-5-2");
            note.Info($"{f.Nom} : D{n:00}", "Coupure par STO du variateur.", "NF EN 61800-5-2");
            return;
        }

        var contacteur = depart.Materiel.Parcourir().FirstOrDefault(e => e.Famille == "Contacteur");
        if (!Redondant(f.PLr))
        {
            if (contacteur is null)
                note.Verifier($"{f.Nom} : D{n:00}", false, "Aucun contacteur sur ce départ : la fonction ne peut pas couper la puissance.",
                    siEchec: StatutCalcul.Alerte);
            else
                note.Info($"{f.Nom} : D{n:00}", $"Coupure par {contacteur.Repere}.");
            return;
        }

        // Catégories 3 et 4 : deux contacteurs en série dont les contacts miroirs sont rebouclés sur la logique.
        if (depart.Materiel.Parcourir().Any(e => e.Repere == $"KM{n}.S")) return;
        var amont = contacteur ?? depart.Materiel;
        var calibre = contacteur?.Calibre ?? $"AC-3 ≥ {A(depart.CourantEmploi)}";
        var redondant = new ElementMateriel { Repere = $"KM{n}.S", Famille = "Contacteur", Designation = "Contacteur de sécurité redondant",
            Calibre = $"{calibre}, contacts miroirs", Statut = amont.Statut, DepartId = depart.Depart.Id };
        // Le contacteur redondant s'intercale entre l'appareil amont et ses appareils aval.
        redondant.Enfants.AddRange(amont.Enfants);
        amont.Enfants.Clear();
        amont.Enfants.Add(redondant);
        depart.Note.Info($"Sécurité : {f.Nom}", $"Deux contacteurs en série ({amont.Repere} et {redondant.Repere}) " +
            "avec contacts miroirs rebouclés sur la logique de sécurité.", "NF EN 60947-4-1, annexe F ; " + Ref);
        note.Info($"{f.Nom} : D{n:00}", $"Coupure redondante par {amont.Repere} et {redondant.Repere}.");
    }

    static string Capteurs(TypeFonctionSecurite type) => type switch
    {
        TypeFonctionSecurite.ArretUrgence => "boutons à accrochage NF EN ISO 13850, arrêt de catégorie 0 ou 1 (NF EN 60204-1).",
        TypeFonctionSecurite.ProtecteurMobile => "interrupteurs de position à ouverture forcée ou codés (NF EN ISO 14119).",
        TypeFonctionSecurite.BarriereImmaterielle => "barrière type 4 pour PL e, type 2 jusqu'à PL c, sorties OSSD (NF EN IEC 61496).",
        TypeFonctionSecurite.CommandeBimanuelle => "commande bimanuelle type IIIC pour PL e (NF EN ISO 13851).",
        TypeFonctionSecurite.ArretSurCouple => "fonction STO du variateur (NF EN 61800-5-2).",
        TypeFonctionSecurite.ScrutateurLaser => "scrutateur laser type 3, PL d au plus, sorties OSSD (NF EN IEC 61496-3).",
        _ => "capteurs à définir.",
    };

    static string EntreesSpecifiques(TypeFonctionSecurite type) => type switch
    {
        TypeFonctionSecurite.BarriereImmaterielle => " à entrées OSSD",
        TypeFonctionSecurite.CommandeBimanuelle => " pour commande bimanuelle",
        _ => "",
    };

    public static string FamilleRelais(FabricantSecurite f) => f switch
    {
        FabricantSecurite.Pilz => "Pilz PNOZ",
        FabricantSecurite.Schmersal => "Schmersal SRB",
        FabricantSecurite.Siemens => "Siemens SIRIUS 3SK1",
        _ => "Schneider Preventa XPS",
    };

    public static string FamilleAutomate(FabricantSecurite f) => f switch
    {
        FabricantSecurite.Pilz => "Pilz PNOZmulti 2",
        FabricantSecurite.Schmersal => "Schmersal PROTECT PSC1",
        FabricantSecurite.Siemens => "Siemens SIRIUS 3SK2 ou CPU de sécurité F",
        _ => "Schneider Preventa XPSMCM",
    };
}
