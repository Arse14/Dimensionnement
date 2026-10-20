using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

public class ResultatAutomate
{
    public required NoteDeCalcul Note { get; init; }
    public CpuAutomate? Cpu { get; init; }
    public required IReadOnlyList<ElementMateriel> Appareils { get; init; }
}

/// <summary>
/// Choix de la CPU selon la charge de communication (périphériques réseau et connexions), puis des cartes d'E/S.
/// </summary>
public static class CalculAutomate
{
    const string Ref = "Fiches techniques Siemens / Schneider (catalogue des automates)";

    /// <summary>Connexions réservées à la programmation et au diagnostic (console de programmation, serveur web).</summary>
    public const int ConnexionsReservees = 2;

    /// <summary>Voies par carte d'E/S retenues pour estimer le nombre de cartes.</summary>
    public const int VoiesTorParCarte = 16;
    public const int VoiesAnalogiquesParCarte = 8;

    // Consommations indicatives sur le 24 V, à remplacer par les valeurs du catalogue.
    const double ConsommationCpu = 0.5;
    const double ConsommationCarte = 0.05;

    public static int Peripheriques(ConfigurationAutomate c) => c.PeripheriquesReseau + c.VariateursBus;
    public static int Connexions(ConfigurationAutomate c) => c.Ihm + c.ClientsOpcUa + c.EchangesAutomates + ConnexionsReservees;

    public static int CartesTor(ConfigurationAutomate c, double reserve) =>
        Cartes(c.EntreesTor, reserve, VoiesTorParCarte) + Cartes(c.SortiesTor, reserve, VoiesTorParCarte);

    public static int CartesAnalogiques(ConfigurationAutomate c, double reserve) =>
        Cartes(c.EntreesAnalogiques, reserve, VoiesAnalogiquesParCarte) + Cartes(c.SortiesAnalogiques, reserve, VoiesAnalogiquesParCarte);

    static int Cartes(int voies, double reserve, int parCarte) =>
        voies <= 0 ? 0 : (int)Math.Ceiling(voies * reserve / parCarte);

    public static IEnumerable<ConsommationCommande> Consommations(ConfigurationAutomate c)
    {
        if (!c.Actif) yield break;
        var reserve = 1 + c.ReservePourcent / 100;
        yield return new ConsommationCommande("CPU automate (estimation)", ConsommationCpu);
        var cartes = CartesTor(c, reserve) + CartesAnalogiques(c, reserve);
        if (cartes > 0)
            yield return new ConsommationCommande($"Cartes d'E/S × {cartes} (estimation, hors capteurs)", cartes * ConsommationCarte);
    }

    public static ResultatAutomate Calculer(ConfigurationAutomate c, IReadOnlyList<CpuAutomate> catalogue)
    {
        var note = new NoteDeCalcul("API");
        if (!c.Actif) return new ResultatAutomate { Note = note, Appareils = Array.Empty<ElementMateriel>() };

        var reserve = 1 + c.ReservePourcent / 100;
        var peripheriques = Peripheriques(c);
        var connexions = Connexions(c);
        var peripheriquesRequis = (int)Math.Ceiling(peripheriques * reserve);
        var connexionsRequises = (int)Math.Ceiling(connexions * reserve);
        note.Info("Charge de communication",
            $"{peripheriques} périphérique(s) réseau ({c.PeripheriquesReseau} îlot(s) ou esclave(s) + {c.VariateursBus} variateur(s)) ; " +
            $"{connexions} connexion(s) ({c.Ihm} IHM + {c.ClientsOpcUa} OPC UA / supervision + {c.EchangesAutomates} échange(s) " +
            $"entre automates + {ConnexionsReservees} programmation et diagnostic).");
        note.Info("Besoin avec réserve", $"Réserve {N(c.ReservePourcent)} % : ≥ {peripheriquesRequis} périphérique(s) et ≥ {connexionsRequises} connexion(s).");

        var candidates = catalogue.Where(cpu => cpu.Marque.Equals(c.Marque.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
        CpuAutomate? choix = null;
        foreach (var cpu in candidates)
        {
            if (c.SecuriteIntegree && cpu.VersionSecurite == false) continue;
            if (c.ClientsOpcUa > 0 && cpu.OpcUa == false) continue;
            if (cpu.PeripheriquesReseauMax is { } p && p < peripheriquesRequis) continue;
            if (cpu.ConnexionsMax is { } n && n < connexionsRequises) continue;
            choix = cpu;
            break;
        }

        var appareils = new List<ElementMateriel>();
        if (choix is null)
        {
            note.Verifier("Choix de la CPU", false,
                $"Aucune CPU {c.Marque} du catalogue ne couvre ce besoin : répartir sur plusieurs automates ou compléter le catalogue.", Ref);
            return new ResultatAutomate { Note = note, Appareils = appareils };
        }

        var nom = c.SecuriteIntegree && c.Marque == MarqueAutomate.Siemens ? $"{choix.Cpu} F" : choix.Cpu;
        note.Info("CPU retenue", $"{choix.Gamme} {nom} ({choix.Reference}) : " +
            $"{Capacite(choix.PeripheriquesReseauMax)} périphériques, {Capacite(choix.ConnexionsMax)} connexions, " +
            $"OPC UA {(choix.OpcUa switch { true => "oui", false => "non", _ => "non renseigné" })}.", Ref);
        if (c.SecuriteIntegree)
            note.Info("Sécurité intégrée", "Prévoir une CPU de sécurité et des cartes d'E/S de sécurité (F-DI / F-DQ ou équivalent Schneider).");

        var inconnues = new List<string>();
        if (choix.PeripheriquesReseauMax is null && peripheriques > 0) inconnues.Add("nombre de périphériques");
        if (choix.ConnexionsMax is null) inconnues.Add("nombre de connexions");
        if (c.ClientsOpcUa > 0 && choix.OpcUa is null) inconnues.Add("serveur OPC UA");
        note.Verifier("Capacités de la CPU", inconnues.Count == 0,
            inconnues.Count == 0 ? "Toutes les limites utiles sont renseignées." : $"Non renseigné dans le catalogue : {string.Join(", ", inconnues)}.",
            Ref, StatutCalcul.Alerte);
        note.Verifier("Catalogue vérifié", choix.Verifie,
            choix.Verifie ? $"Valeurs contrôlées ({choix.Source})." : "Valeurs indicatives : à contrôler sur la fiche technique avant de commander.",
            Ref, StatutCalcul.Alerte);

        var cartesTor = CartesTor(c, reserve);
        var cartesAna = CartesAnalogiques(c, reserve);
        note.Info("Cartes d'E/S", $"{cartesTor} carte(s) TOR de {VoiesTorParCarte} voies et {cartesAna} carte(s) analogiques de " +
            $"{VoiesAnalogiquesParCarte} voies, réserve {N(c.ReservePourcent)} % incluse.");

        var cpuElement = new ElementMateriel { Repere = "API1", Famille = "Automate", Designation = $"{choix.Gamme} {nom}",
            Calibre = choix.Reference, Fabricant = c.Marque.ToString(), Reference = choix.Reference };
        if (cartesTor > 0)
            cpuElement.Ajouter(new ElementMateriel { Repere = "API1-TOR", Famille = "Entrées/sorties",
                Designation = $"Carte d'E/S TOR {VoiesTorParCarte} voies", Calibre = c.Marque.ToString(), Quantite = cartesTor });
        if (cartesAna > 0)
            cpuElement.Ajouter(new ElementMateriel { Repere = "API1-ANA", Famille = "Entrées/sorties",
                Designation = $"Carte d'E/S analogiques {VoiesAnalogiquesParCarte} voies", Calibre = c.Marque.ToString(), Quantite = cartesAna });
        appareils.Add(cpuElement);
        return new ResultatAutomate { Note = note, Cpu = choix, Appareils = appareils };
    }

    static string Capacite(int? valeur) => valeur?.ToString() ?? "?";
}
