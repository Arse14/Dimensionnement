using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public record ResultatFonctionSecurite(FonctionSecurite Fonction, string Categorie, string Architecture);

public class ResultatSecurite
{
    public required NoteDeCalcul Note { get; init; }
    public required IReadOnlyList<ResultatFonctionSecurite> Fonctions { get; init; }

    /// <summary>Appareils de sécurité (relais ou automate) à raccorder au 24 V.</summary>
    public required IReadOnlyList<ElementMateriel> Appareils { get; init; }
    public required IReadOnlyList<ConsommationCommande> Consommations { get; init; }
}

/// <summary>
/// Préconisations de sécurité machine selon la NF EN ISO 13849-1 : catégorie d'architecture, logique de sécurité
/// (relais ou automate) et partie puissance. Le PL atteint reste à calculer et valider dans SISTEMA.
/// </summary>
public static class CalculSecurite
{
    const string Ref = "NF EN ISO 13849-1";

    /// <summary>Au-delà de ce nombre de fonctions, un automate de sécurité configurable est préconisé.</summary>
    public const int SeuilAutomate = 3;

    // Consommations indicatives sur le 24 V, à remplacer par les valeurs du catalogue.
    const double ConsommationRelais = 0.1;
    const double ConsommationAutomate = 0.25;
    const double ConsommationParFonction = 0.03;

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

    public static ResultatSecurite Calculer(Projet projet, IReadOnlyList<ResultatDepart> departs)
    {
        var config = projet.Securite;
        var note = new NoteDeCalcul("SEC");
        var fonctions = new List<ResultatFonctionSecurite>();
        var appareils = new List<ElementMateriel>();
        var consommations = new List<ConsommationCommande>();
        if (config.Fonctions.Count == 0)
            return new ResultatSecurite { Note = note, Fonctions = fonctions, Appareils = appareils, Consommations = consommations };

        var automate = AvecAutomate(config);
        consommations.AddRange(Consommations(config));
        note.Info("Logique de sécurité", automate
            ? $"{config.Fonctions.Count} fonction(s) : automate de sécurité configurable {FamilleAutomate(config.Fabricant)}."
            : $"{config.Fonctions.Count} fonction(s) : un relais de sécurité par fonction ({FamilleRelais(config.Fabricant)}).", Ref);

        if (automate)
        {
            appareils.Add(new ElementMateriel { Repere = "KA-SEC", Famille = "Sécurité", Designation = "Automate de sécurité",
                Calibre = FamilleAutomate(config.Fabricant), Fabricant = config.Fabricant.ToString() });
        }

        var i = 0;
        foreach (var f in config.Fonctions)
        {
            i++;
            var categorie = Categorie(f.PLr);
            var architecture = Redondant(f.PLr)
                ? "deux canaux, surveillance des contacts miroirs, détection des défauts"
                : f.PLr == NiveauPerformance.c ? "un canal avec fonction de test" : "un canal, composants éprouvés";
            note.Info($"{f.Nom} (PLr {f.PLr})", $"Catégorie {categorie} conseillée : {architecture}. " +
                $"{f.NombreCapteurs} capteur(s) ; {Capteurs(f.Type)}", Ref);
            fonctions.Add(new ResultatFonctionSecurite(f, categorie, architecture));

            if (!automate)
            {
                appareils.Add(new ElementMateriel { Repere = $"KA{i}", Famille = "Sécurité", Designation = $"Relais de sécurité – {f.Nom}",
                    Calibre = $"{FamilleRelais(config.Fabricant)}{EntreesSpecifiques(f.Type)}, cat. {categorie}",
                    Fabricant = config.Fabricant.ToString() });
            }

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
        return new ResultatSecurite { Note = note, Fonctions = fonctions, Appareils = appareils, Consommations = consommations };
    }

    public static bool AvecAutomate(ConfigurationSecurite config) =>
        config.ImposerAutomate || config.Fonctions.Count >= SeuilAutomate;

    /// <summary>Consommation 24 V estimée de la logique de sécurité.</summary>
    public static IEnumerable<ConsommationCommande> Consommations(ConfigurationSecurite config)
    {
        if (config.Fonctions.Count == 0) yield break;
        if (AvecAutomate(config))
        {
            yield return new ConsommationCommande("Automate de sécurité (estimation)",
                ConsommationAutomate + ConsommationParFonction * config.Fonctions.Count);
            yield break;
        }
        foreach (var f in config.Fonctions)
            yield return new ConsommationCommande($"Relais de sécurité {f.Nom} (estimation)", ConsommationRelais);
    }

    /// <summary>Ajoute à un départ les appareils de coupure qu'impose la fonction de sécurité.</summary>
    static void PartiePuissance(FonctionSecurite f, ResultatDepart depart, NoteDeCalcul note)
    {
        var n = depart.Depart.Numero;
        var variateur = depart.Depart is DepartMoteur { Demarrage: ModeDemarrage.Variateur };
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
