using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using static Dimensionnement.Core.Calculs.Format;

namespace Dimensionnement.Core.Calculs;

/// <summary>Voies d'un type : saisies, requises avec la réserve, prises sur la CPU et reportées sur des cartes.</summary>
public record BesoinVoies(string Type, int Saisies, int Requises, int Integrees, List<CarteEs> Cartes)
{
    public int SurCartes => Requises - Integrees;
    public int Disponibles => Integrees + Cartes.Sum(c => c.Voies);
}

/// <summary>
/// Cartes d'E/S d'une CPU : famille retenue, tête de station ET200SP éventuelle, châssis, cartes et capacité d'accueil.
/// </summary>
public record PlanCartes(CpuAutomate Cpu, string Famille, CarteEs? Interface, CarteEs? Chassis, CarteEs? AlimentationChassis,
    List<BesoinVoies> Besoins, int? Capacite, List<string> Manquantes)
{
    public bool Station => Famille == "ET200SP" && !Cpu.Cartes.Equals("ET200SP", StringComparison.OrdinalIgnoreCase);
    public List<CarteEs> Cartes => Besoins.SelectMany(b => b.Cartes).ToList();
    public bool Tient => Capacite is null || Cartes.Count <= Capacite;
}

/// <summary>Une station d'E/S déportée : sa tête de station et ses cartes.</summary>
public record PlanStation(StationDeportee Station, string Famille, CarteEs? Tete, List<BesoinVoies> Besoins, List<string> Manquantes)
{
    public List<CarteEs> Cartes => Besoins.SelectMany(b => b.Cartes).ToList();
    public bool Tient => Tete is null || Cartes.Count <= Tete.Voies;
}

public class ResultatAutomate
{
    public required NoteDeCalcul Note { get; init; }
    public CpuAutomate? Cpu { get; init; }
    public PlanCartes? Plan { get; init; }
    public IReadOnlyList<PlanStation> Stations { get; init; } = Array.Empty<PlanStation>();
    public required IReadOnlyList<ElementMateriel> Appareils { get; init; }
    public IReadOnlyList<ConsommationCommande> Consommations { get; init; } = Array.Empty<ConsommationCommande>();
}

/// <summary>
/// Choix de la CPU selon la charge de communication et la place pour les cartes d'E/S, ou CPU imposée par l'utilisateur ;
/// puis cartes d'E/S standard de la famille de la CPU, ajoutées au fur et à mesure des voies nécessaires.
/// </summary>
public static class CalculAutomate
{
    const string Ref = "Fiches techniques Siemens / Schneider (catalogues des automates et des cartes d'E/S)";

    /// <summary>Connexions réservées à la programmation et au diagnostic (console de programmation, serveur web).</summary>
    public const int ConnexionsReservees = 2;

    // Consommations indicatives sur le 24 V quand le catalogue ne les donne pas.
    const double ConsommationCpu = 0.5;
    const double ConsommationCarte = 0.05;

    public static int Peripheriques(ConfigurationAutomate c, int ilotsReseau = 0) =>
        c.PeripheriquesReseau + c.VariateursBus + c.Stations.Count + ilotsReseau;
    public static int Connexions(ConfigurationAutomate c) => NombreIhm(c) + c.ClientsOpcUa + c.EchangesAutomates + ConnexionsReservees;

    /// <summary>Pupitres IHM : ceux choisis au catalogue et ceux déclarés sans modèle.</summary>
    public static int NombreIhm(ConfigurationAutomate c) => c.Ihm + c.Pupitres.Sum(p => Math.Max(0, p.Quantite));

    public static int Voies(ConfigurationAutomate c, string type) => type switch
    {
        "DI" => c.EntreesTor,
        "DQ" => c.SortiesTor,
        "AI" => c.EntreesAnalogiques,
        "AQ" => c.SortiesAnalogiques,
        _ => 0,
    };

    /// <summary>Voies requises avec la réserve.</summary>
    public static int Requises(int voies, double reserve) => voies <= 0 ? 0 : (int)Math.Ceiling(voies * reserve - 1e-9);

    public static IReadOnlyList<ConsommationCommande> Consommations(ConfigurationAutomate c, IReadOnlyList<CpuAutomate>? catalogue = null,
        IReadOnlyList<CarteEs>? cartes = null, IReadOnlyList<ModeleIhm>? ihm = null) =>
        c.Actif ? Calculer(c, catalogue ?? CatalogueAutomates.Charger(), cartes, ihm).Consommations : Array.Empty<ConsommationCommande>();

    /// <summary>
    /// Cartes d'un type pour couvrir un nombre de voies : la plus grande carte standard tant qu'il en reste plus qu'elle
    /// n'en porte, puis la plus petite qui couvre le reste. Null si la famille n'a pas de carte de ce type.
    /// </summary>
    public static List<CarteEs>? ChoisirCartes(string famille, string type, int voies, bool compactes, IReadOnlyList<CarteEs> catalogue)
    {
        var cartes = new List<CarteEs>();
        if (voies <= 0) return cartes;
        var dispo = CatalogueCartesEs.De(famille, type, catalogue).Where(c => c.Voies > 0 && !c.Specifique && (compactes || !c.Compacte))
            .OrderBy(c => c.Voies).ThenBy(c => c.Largeur).ToList();
        if (dispo.Count == 0) return null;
        var grande = dispo.Where(c => c.Voies == dispo[^1].Voies).OrderBy(c => c.Largeur).First();
        var reste = voies;
        while (reste > grande.Voies)
        {
            cartes.Add(grande);
            reste -= grande.Voies;
        }
        cartes.Add(dispo.First(c => c.Voies >= reste));
        return cartes;
    }

    static List<BesoinVoies> Besoins(Func<string, int> voies, Func<string, int> integreesCpu, string famille, bool compactes,
        double reserve, IReadOnlyList<CarteEs> catalogue, List<string> manquantes)
    {
        var besoins = new List<BesoinVoies>();
        foreach (var type in CatalogueCartesEs.TypesVoies)
        {
            var saisies = voies(type);
            var requises = Requises(saisies, reserve);
            var integrees = Math.Min(requises, integreesCpu(type));
            var cartes = ChoisirCartes(famille, type, requises - integrees, compactes, catalogue);
            if (cartes is null) manquantes.Add(CatalogueCartesEs.Libelle(type));
            besoins.Add(new BesoinVoies(type, saisies, requises, integrees, cartes ?? new List<CarteEs>()));
        }
        return besoins;
    }

    /// <summary>Famille des stations déportées : ET200SP chez Siemens, îlot TM3 sur coupleur chez Schneider.</summary>
    public static string FamilleStations(string marque) =>
        marque.Equals("Siemens", StringComparison.OrdinalIgnoreCase) ? "ET200SP" : "Modicon TM3";

    public static List<PlanStation> PlanifierStations(ConfigurationAutomate c, string marque, IReadOnlyList<CarteEs> catalogue)
    {
        var reserve = 1 + c.ReservePourcent / 100;
        var famille = FamilleStations(marque);
        var tete = CatalogueCartesEs.De(famille, "Interface", catalogue).FirstOrDefault();
        return c.Stations.Select(s =>
        {
            var manquantes = new List<string>();
            if (tete is null) manquantes.Add($"tête de station {famille}");
            var besoins = Besoins(t => t switch
            {
                "DI" => s.EntreesTor, "DQ" => s.SortiesTor, "AI" => s.EntreesAnalogiques, "AQ" => s.SortiesAnalogiques, _ => 0,
            }, _ => 0, famille, c.CartesCompactes, reserve, catalogue, manquantes);
            return new PlanStation(s, famille, tete, besoins, manquantes);
        }).ToList();
    }

    /// <summary>
    /// Octets d'E/S échangés en entrée et en sortie : 1 octet par 8 voies TOR (arrondi par carte), 2 octets par voie analogique.
    /// </summary>
    public static (int Entrees, int Sorties) Octets(IEnumerable<BesoinVoies> besoins)
    {
        int entrees = 0, sorties = 0;
        foreach (var b in besoins)
        {
            var tor = b.Type[0] == 'D';
            int Taille(int voies) => tor ? (voies + 7) / 8 : 2 * voies;
            var octets = Taille(b.Integrees) + b.Cartes.Sum(x => Taille(x.Voies));
            if (b.Type[1] == 'I') entrees += octets; else sorties += octets;
        }
        return (entrees, sorties);
    }

    /// <summary>Cartes nécessaires autour d'une CPU donnée.</summary>
    public static PlanCartes Planifier(CpuAutomate cpu, ConfigurationAutomate c, IReadOnlyList<CarteEs> catalogue)
    {
        var reserve = 1 + c.ReservePourcent / 100;
        var siemens = cpu.Marque.Equals("Siemens", StringComparison.OrdinalIgnoreCase);
        var famille = c.CartesCompactes && siemens ? "ET200SP" : cpu.Cartes;
        var manquantes = new List<string>();
        var besoins = Besoins(t => Voies(c, t), cpu.Integrees, famille, c.CartesCompactes, reserve, catalogue, manquantes);
        var nombre = besoins.Sum(b => b.Cartes.Count);

        var station = famille == "ET200SP" && !cpu.Cartes.Equals("ET200SP", StringComparison.OrdinalIgnoreCase);
        CarteEs? tete = null, chassis = null, alimentation = null;
        var capacite = cpu.CartesMax;
        if (station && nombre > 0)
        {
            tete = CatalogueCartesEs.De(famille, "Interface", catalogue).FirstOrDefault();
            if (tete is null) manquantes.Add("tête de station ET200SP");
            capacite = tete?.Voies;
        }
        var chassisFamille = CatalogueCartesEs.De(famille, "Chassis", catalogue).OrderBy(x => x.Voies).ToList();
        if (chassisFamille.Count > 0)
        {
            chassis = chassisFamille.FirstOrDefault(x => x.Voies >= nombre) ?? chassisFamille[^1];
            alimentation = CatalogueCartesEs.De(famille, "AlimentationChassis", catalogue).FirstOrDefault();
            capacite = Math.Min(capacite ?? int.MaxValue, chassisFamille[^1].Voies);
        }
        return new PlanCartes(cpu, famille, tete, chassis, alimentation, besoins, capacite, manquantes);
    }

    public static ResultatAutomate Calculer(ConfigurationAutomate c, IReadOnlyList<CpuAutomate> catalogue, IReadOnlyList<CarteEs>? cartes = null,
        IReadOnlyList<ModeleIhm>? ihm = null, int ilotsReseau = 0)
    {
        var note = new NoteDeCalcul("API") { Groupe = "CPU" };
        if (!c.Actif) return new ResultatAutomate { Note = note, Appareils = Array.Empty<ElementMateriel>() };
        cartes ??= CatalogueCartesEs.Charger();

        var reserve = 1 + c.ReservePourcent / 100;
        var peripheriques = Peripheriques(c, ilotsReseau);
        var connexions = Connexions(c);
        var peripheriquesRequis = (int)Math.Ceiling(peripheriques * reserve);
        var connexionsRequises = (int)Math.Ceiling(connexions * reserve);
        note.Info("Charge de communication",
            $"{peripheriques} périphérique(s) réseau ({c.Stations.Count} station(s) déportée(s) + {c.PeripheriquesReseau} autre(s) esclave(s) " +
            $"+ {c.VariateursBus} variateur(s)" + (ilotsReseau > 0 ? $" + {ilotsReseau} îlot(s) d'électrovannes" : "") + ") ; " +
            $"{connexions} connexion(s) ({NombreIhm(c)} IHM + {c.ClientsOpcUa} OPC UA / supervision + {c.EchangesAutomates} échange(s) " +
            $"entre automates + {ConnexionsReservees} programmation et diagnostic).");
        note.Info("Besoin avec réserve", $"Réserve {N(c.ReservePourcent)} % : ≥ {peripheriquesRequis} périphérique(s) et ≥ {connexionsRequises} connexion(s).");

        CpuAutomate? choix = null;
        PlanCartes? plan = null;
        var imposee = c.CpuImposee.Trim();
        if (imposee.Length > 0)
        {
            choix = catalogue.FirstOrDefault(x => x.Reference.Equals(imposee, StringComparison.OrdinalIgnoreCase));
            if (choix is null)
                note.Verifier("CPU imposée", false, $"Référence {imposee} absente du catalogue des CPU : choix automatique.", Ref, StatutCalcul.Alerte);
            else
            {
                plan = Planifier(choix, c, cartes);
                note.Info("CPU imposée", $"{choix.Libelle} : les cartes d'E/S suivent sa famille ({plan.Famille}).");
                if (!choix.Marque.Equals(c.Marque.ToString(), StringComparison.OrdinalIgnoreCase))
                    note.Info("Marque", $"La CPU imposée est une {choix.Marque} : elle prime sur la marque {c.Marque} saisie.");
                var versionF = catalogue.FirstOrDefault(x => x.Securite && x.Cpu.Replace(" F-", "-").Replace("F-", "-") == choix.Cpu);
                note.Verifier("Sécurité intégrée", !(c.SecuriteIntegree && (choix.VersionSecurite == false || versionF is not null)),
                    !c.SecuriteIntegree ? "Non demandée."
                    : choix.Securite ? $"La {choix.Cpu} est la version de sécurité."
                    : versionF is not null ? $"La {choix.Cpu} est la version standard : imposer la {versionF.Cpu} ({versionF.Reference})."
                    : $"La {choix.Cpu} {(choix.VersionSecurite == false ? "n'existe pas" : "existe")} en version de sécurité.", Ref);
                note.Verifier("OPC UA", !(c.ClientsOpcUa > 0 && choix.OpcUa == false),
                    c.ClientsOpcUa > 0 ? $"Serveur OPC UA {(choix.OpcUa == false ? "absent" : "disponible")} sur la {choix.Cpu}." : "Non demandé.", Ref);
                var periph = peripheriquesRequis + (plan.Station ? 1 : 0);
                note.Verifier("Périphériques réseau", choix.PeripheriquesReseauMax is not { } p || p >= periph,
                    $"{periph} requis{(plan.Station ? " (station ET200SP comprise)" : "")}, {Capacite(choix.PeripheriquesReseauMax)} admis.", Ref);
                note.Verifier("Connexions", choix.ConnexionsMax is not { } n || n >= connexionsRequises,
                    $"{connexionsRequises} requises, {Capacite(choix.ConnexionsMax)} admises.", Ref);
                var (ei, so) = Octets(plan.Besoins.Concat(PlanifierStations(c, choix.Marque, cartes).SelectMany(x => x.Besoins)));
                note.Verifier("Zone d'adresses d'E/S", choix.ZoneEsOctets is not { } z || Math.Max(ei, so) <= z,
                    $"{ei} octets en entrée et {so} en sortie, {Capacite(choix.ZoneEsOctets)} admis.", Ref);
            }
        }
        if (choix is null)
        {
            var candidates = catalogue.Where(x => x.Marque.Equals(c.Marque.ToString(), StringComparison.OrdinalIgnoreCase));
            // Cartes compactes : les CPU au format ET200SP passent devant ; sinon elles sont réservées à ce choix.
            candidates = c.CartesCompactes ? candidates.OrderByDescending(x => x.Compacte) : candidates.Where(x => !x.Compacte);
            // Sécurité intégrée : les CPU F du catalogue d'abord, à la place de leur version standard ; sinon, les versions standard.
            candidates = c.SecuriteIntegree && candidates.Any(x => x.Securite)
                ? candidates.Where(x => x.Securite).Concat(candidates.Where(x => !x.Securite && !candidates.Any(f => f.Securite && f.Gamme == x.Gamme)))
                : candidates.Where(x => !x.Securite);
            foreach (var cpu in candidates)
            {
                if (c.SecuriteIntegree && cpu.VersionSecurite == false) continue;
                if (c.ClientsOpcUa > 0 && cpu.OpcUa == false) continue;
                var p = Planifier(cpu, c, cartes);
                if (cpu.PeripheriquesReseauMax is { } max && max < peripheriquesRequis + (p.Station ? 1 : 0)) continue;
                if (cpu.ConnexionsMax is { } n && n < connexionsRequises) continue;
                if (!p.Tient) continue;
                var (ei, so) = Octets(p.Besoins.Concat(PlanifierStations(c, cpu.Marque, cartes).SelectMany(x => x.Besoins)));
                if (cpu.ZoneEsOctets is { } z && Math.Max(ei, so) > z) continue;
                choix = cpu;
                plan = p;
                break;
            }
        }

        var appareils = new List<ElementMateriel>();
        if (choix is null || plan is null)
        {
            note.Verifier("Choix de la CPU", false,
                $"Aucune CPU {c.Marque} du catalogue ne couvre ce besoin (communication et cartes d'E/S) : répartir sur plusieurs " +
                "automates, passer en cartes compactes ou compléter le catalogue.", Ref);
            var estimation = Estimation(c);
            Pupitres(c, c.Marque.ToString(), ihm ?? CatalogueIhm.Charger(), note, appareils, estimation);
            return new ResultatAutomate { Note = note, Appareils = appareils, Consommations = estimation };
        }

        // CPU de sécurité demandée mais absente du catalogue en version F : le nom porte « F », la référence reste celle de la
        // version standard et doit être remplacée par la référence F du fabricant.
        var sansVersionF = c.SecuriteIntegree && choix.Marque == "Siemens" && !choix.Securite;
        var nom = sansVersionF ? $"{choix.Cpu} F" : choix.Cpu;
        if (sansVersionF)
            note.Verifier("Version F de la CPU", false,
                $"Le catalogue n'a pas la version F de la {choix.Cpu} : la référence {choix.Reference} est celle de la version standard, " +
                "à remplacer par la référence F du fabricant.", Ref, StatutCalcul.Alerte);
        note.Info("CPU retenue", $"{choix.Gamme} {nom} ({choix.Reference}) : " +
            $"{Capacite(choix.PeripheriquesReseauMax)} périphériques, {Capacite(choix.ConnexionsMax)} connexions, " +
            $"OPC UA {(choix.OpcUa switch { true => "oui", false => "non", _ => "non renseigné" })}.", Ref);
        if (c.SecuriteIntegree)
            note.Info("Sécurité intégrée", "Prévoir une CPU de sécurité et des cartes d'E/S de sécurité (F-DI / F-DQ ou équivalent Schneider).");

        var inconnues = new List<string>();
        if (choix.PeripheriquesReseauMax is null && peripheriques > 0) inconnues.Add("nombre de périphériques");
        if (choix.ConnexionsMax is null) inconnues.Add("nombre de connexions");
        if (c.ClientsOpcUa > 0 && choix.OpcUa is null) inconnues.Add("serveur OPC UA");
        if (choix.CartesMax is null && plan.Cartes.Count > 0 && !plan.Station) inconnues.Add("nombre de cartes d'E/S");
        note.Verifier("Capacités de la CPU", inconnues.Count == 0,
            inconnues.Count == 0 ? "Toutes les limites utiles sont renseignées." : $"Non renseigné dans le catalogue : {string.Join(", ", inconnues)}.",
            Ref, StatutCalcul.Alerte);

        // Bilan des voies par type et cartes ajoutées.
        note.Groupe = "Cartes d'E/S";
        foreach (var b in plan.Besoins.Where(b => b.Saisies > 0))
        {
            var detail = $"{b.Saisies} × {N(reserve)} = {b.Requises} voie(s)";
            if (b.Integrees > 0) detail += $", dont {b.Integrees} intégrée(s) à la CPU";
            detail += b.Cartes.Count == 0
                ? (b.SurCartes > 0 ? " : aucune carte de ce type au catalogue." : " : pas de carte.")
                : $" ; {b.SurCartes} sur cartes : {Liste(b.Cartes)}, soit {b.Disponibles} voie(s) disponibles.";
            note.Info(CatalogueCartesEs.Libelle(b.Type), detail, Ref);
        }
        if (plan.Manquantes.Count > 0)
            note.Verifier("Cartes au catalogue", false,
                $"Famille {plan.Famille} : rien au catalogue pour {string.Join(", ", plan.Manquantes)}.", Ref, StatutCalcul.Alerte);
        if (plan.Station && plan.Interface is { } im)
            note.Info("Station ET200SP", $"Cartes compactes ET200SP déportées sur une tête {im.Designation} ({im.Reference}), " +
                "à côté de la CPU et reliée en PROFINET : un périphérique réseau de plus.", Ref);
        if (plan.Chassis is { } ch)
            note.Info("Châssis", $"{ch.Designation} ({ch.Reference}) : {plan.Cartes.Count} module(s) pour {ch.Voies} emplacement(s) libres.", Ref);
        var nombre = plan.Cartes.Count;
        if (nombre > 0)
            note.Verifier("Cartes d'E/S admises", plan.Tient,
                $"{nombre} carte(s) pour {Capacite(plan.Capacite)} admise(s) " +
                (plan.Station ? "par la tête de station." : plan.Chassis is not null ? "dans le châssis." : "à côté de la CPU.") +
                (plan.Tient ? "" : " Choisir une CPU qui en admet plus, ou cocher les cartes compactes (station ET200SP)."), Ref);

        var stations = PlanifierStations(c, choix.Marque, cartes);
        foreach (var st in stations)
        {
            var cartesStation = st.Cartes;
            note.Verifier($"Station {st.Station.Nom}", st.Tient && st.Manquantes.Count == 0,
                $"{st.Tete?.Designation ?? "tête de station absente du catalogue"} ({st.Tete?.Reference}) : " +
                (cartesStation.Count == 0 ? "aucune carte" : Liste(cartesStation)) +
                $" ; {cartesStation.Count} carte(s) pour {Capacite(st.Tete?.Voies)} admises" +
                (st.Station.DansArmoire ? ", dans l'armoire." : ", en coffret sur la machine.") +
                (st.Tient ? "" : " Répartir ses E/S sur une station de plus."), Ref);
        }
        var (octetsEntree, octetsSortie) = Octets(plan.Besoins.Concat(stations.SelectMany(x => x.Besoins)));
        note.Verifier("Zone d'adresses d'E/S", choix.ZoneEsOctets is not { } zone || Math.Max(octetsEntree, octetsSortie) <= zone,
            $"{octetsEntree} octets en entrée et {octetsSortie} en sortie (CPU, cartes et stations), {Capacite(choix.ZoneEsOctets)} admis.", Ref,
            StatutCalcul.NonConforme);

        var cartesVerifiees = plan.Cartes.Concat(stations.SelectMany(x => x.Cartes)).All(x => x.Verifie);
        note.Verifier("Catalogue vérifié", choix.Verifie && cartesVerifiees,
            choix.Verifie && cartesVerifiees ? $"Valeurs contrôlées ({choix.Source})." : "Valeurs indicatives : à contrôler sur la fiche technique avant de commander.",
            Ref, StatutCalcul.Alerte);

        var marque = choix.Marque;
        var cpuElement = new ElementMateriel { Repere = "API1", Famille = "Automate", Designation = $"{choix.Gamme} {nom}",
            Calibre = choix.Reference, Fabricant = marque, Reference = choix.Reference,
            LargeurMm = choix.Largeur, HauteurMm = choix.Hauteur, ProfondeurMm = choix.Profondeur, PertesW = choix.Pertes,
            Loge = choix.Largeur <= 0 && plan.Chassis is not null, AVerifier = sansVersionF };
        appareils.Add(cpuElement);
        var consommations = new List<ConsommationCommande>
        {
            new($"Automate {choix.Cpu} ({choix.Reference})", choix.Consommation > 0 ? choix.Consommation : ConsommationCpu),
        };

        var parent = cpuElement;
        if (plan.Chassis is { } chassis)
        {
            cpuElement.Ajouter(Element("API1-CH", "Entrées/sorties", chassis, marque, $"{chassis.Voies} emplacements"));
            if (plan.AlimentationChassis is { } ps)
            {
                cpuElement.Ajouter(Element("API1-PS", "Entrées/sorties", ps, marque, "alimentation du châssis"));
                consommations.Add(new($"Alimentation de châssis {ps.Reference}", ps.Consommation));
            }
        }
        var prefixe = "API1";
        if (plan.Interface is { } tete)
        {
            parent = cpuElement.Ajouter(Element("API1-ET", "Entrées/sorties", tete, marque, $"station ET200SP, {tete.Voies} cartes max."));
            if (tete.Accessoire.Length > 0)
                parent.Ajouter(Accessoire("API1-ET-X", tete.Accessoire, tete.AccessoireDesignation, marque));
            consommations.Add(new($"Tête de station {tete.Reference}", tete.Consommation));
            prefixe = "API1-ET";
        }
        var i = 0;
        foreach (var carte in plan.Cartes)
        {
            i++;
            var repere = $"{prefixe}.{i}";
            var element = parent.Ajouter(Element(repere, "Entrées/sorties", carte, marque, $"{carte.Voies} voies {carte.Type}"));
            var (accessoire, designation) = i == 1 && carte.AccessoireDebut.Length > 0
                ? (carte.AccessoireDebut, carte.AccessoireDebutDesignation)
                : (carte.Accessoire, carte.AccessoireDesignation);
            if (accessoire.Length > 0) element.Ajouter(Accessoire($"{repere}-X", accessoire, designation, marque));
        }
        foreach (var g in plan.Cartes.GroupBy(x => x.Reference))
        {
            var unitaire = g.First().Consommation > 0 ? g.First().Consommation : ConsommationCarte;
            consommations.Add(new($"Cartes {g.Key} × {g.Count()} (hors capteurs)", unitaire * g.Count()));
        }
        var numero = 0;
        foreach (var st in stations)
        {
            numero++;
            var repereStation = $"API1-S{numero}";
            var coupleur = st.Tete;
            var element = cpuElement.Ajouter(new ElementMateriel
            {
                Repere = repereStation, Famille = "Entrées/sorties",
                Designation = $"{st.Station.Nom} – {coupleur?.Designation ?? "station déportée"}",
                Calibre = $"station déportée, {st.Cartes.Count} carte(s)", Fabricant = marque, Reference = coupleur?.Reference ?? "",
                LargeurMm = coupleur?.Largeur ?? 0, HauteurMm = coupleur?.Hauteur ?? 0, ProfondeurMm = coupleur?.Profondeur ?? 0, PertesW = coupleur?.Pertes ?? 0,
            });
            if (coupleur is { Accessoire.Length: > 0 }) element.Ajouter(Accessoire($"{repereStation}-X", coupleur.Accessoire, coupleur.AccessoireDesignation, marque));
            var k = 0;
            foreach (var carte in st.Cartes)
            {
                k++;
                var repere = $"{repereStation}.{k}";
                var e = element.Ajouter(Element(repere, "Entrées/sorties", carte, marque, $"{carte.Voies} voies {carte.Type}"));
                var (accessoire, designation) = k == 1 && carte.AccessoireDebut.Length > 0
                    ? (carte.AccessoireDebut, carte.AccessoireDebutDesignation)
                    : (carte.Accessoire, carte.AccessoireDesignation);
                if (accessoire.Length > 0) e.Ajouter(Accessoire($"{repere}-X", accessoire, designation, marque));
            }
            if (!st.Station.DansArmoire)
                foreach (var x in element.Parcourir()) x.HorsArmoire = true;
            else
                consommations.Add(new($"{st.Station.Nom} : tête et {st.Cartes.Count} carte(s)",
                    (coupleur?.Consommation ?? 0) + st.Cartes.Sum(x => x.Consommation > 0 ? x.Consommation : ConsommationCarte)));
        }
        note.Groupe = "Alimentation 24 V";
        if (stations.Any(x => !x.Station.DansArmoire))
            note.Info("Stations en coffret", "Les stations hors armoire sont reprises au devis mais pas sur la plaque ni au bilan 24 V de l'armoire : " +
                "prévoir leur coffret, leur alimentation et le câble réseau.");
        Pupitres(c, marque, ihm ?? CatalogueIhm.Charger(), note, appareils, consommations);
        note.Info("Consommation 24 V", $"{A(consommations.Sum(x => x.Courant))} pour la CPU, ses cartes et les IHM, hors capteurs et actionneurs.");
        return new ResultatAutomate { Note = note, Cpu = choix, Plan = plan, Stations = stations, Appareils = appareils, Consommations = consommations };
    }

    /// <summary>Consommation indicative d'un pupitre IHM sans modèle, en A sur le 24 V DC.</summary>
    public const double ConsommationIhm = 0.5;

    /// <summary>Pupitres IHM : modèle retenu, consommation sur le 24 V DC et découpe de porte.</summary>
    static void Pupitres(ConfigurationAutomate c, string marque, IReadOnlyList<ModeleIhm> catalogue, NoteDeCalcul note,
        List<ElementMateriel> appareils, List<ConsommationCommande> consommations)
    {
        note.Groupe = "IHM";
        const string RefIhm = "Fiches techniques des pupitres Siemens / Schneider";
        if (c.Ihm > 0)
            consommations.Add(new($"IHM sans modèle choisi × {c.Ihm} (estimation)", ConsommationIhm * c.Ihm));
        var n = 0;
        foreach (var p in c.Pupitres.Where(p => p.Quantite > 0))
        {
            var modele = CatalogueIhm.Choisir(p, marque, catalogue);
            if (modele is null)
            {
                note.Verifier($"IHM {p.Nom}", false, p.ReferenceImposee.Length > 0
                    ? $"Référence {p.ReferenceImposee} absente du catalogue des IHM : gardée telle quelle, cotes et consommation estimées à vérifier."
                    : $"Aucun pupitre {marque} de gamme {p.Gamme} au catalogue.", RefIhm, StatutCalcul.Alerte);
                consommations.Add(new($"IHM {p.Nom} × {p.Quantite} (estimation)", ConsommationIhm * p.Quantite));
                // Une référence imposée reste dans l'arborescence, la porte et le devis, avec des cotes à compléter.
                if (p.ReferenceImposee.Length > 0)
                    for (var k = 0; k < p.Quantite; k++)
                    {
                        n++;
                        appareils.Add(new ElementMateriel
                        {
                            Repere = $"HMI{n}", Famille = "IHM", Designation = $"{p.Nom} – {p.ReferenceImposee}",
                            Calibre = $"{N(p.Diagonale)}\"" + (p.EnPorte ? ", en porte" : ", pupitre machine"),
                            Fabricant = marque, Reference = p.ReferenceImposee, PertesW = 24 * ConsommationIhm,
                            EnPorte = p.EnPorte, HorsArmoire = !p.EnPorte, AVerifier = true, CotesIncompletes = true,
                        });
                    }
                continue;
            }
            note.Verifier($"IHM {p.Nom}", modele.Verifie,
                $"{p.Quantite} × {modele.Gamme} {modele.Libelle}, {modele.Interface} ; façade {N(modele.Largeur, 0)} × {N(modele.Hauteur, 0)} mm, " +
                $"découpe {N(modele.DecoupeLargeur, 0)} × {N(modele.DecoupeHauteur, 0)} mm, {A(modele.Consommation)} sur le 24 V DC" +
                (p.EnPorte ? ", en porte d'armoire." : ", sur pupitre machine.") + (modele.Verifie ? "" : " Valeurs indicatives, à vérifier."),
                RefIhm, StatutCalcul.Alerte);
            if (modele.Diagonale + 0.3 < p.Diagonale && p.ReferenceImposee.Length == 0)
                note.Verifier($"Diagonale de {p.Nom}", false, $"{N(p.Diagonale)}\" demandés, {N(modele.Diagonale)}\" au plus dans la gamme.", RefIhm, StatutCalcul.Alerte);
            consommations.Add(new($"IHM {modele.Designation} × {p.Quantite}", modele.Consommation * p.Quantite));
            for (var k = 0; k < p.Quantite; k++)
            {
                n++;
                appareils.Add(new ElementMateriel
                {
                    Repere = $"HMI{n}", Famille = "IHM", Designation = $"{p.Nom} – {modele.Designation}",
                    Calibre = $"{N(modele.Diagonale)}\", {modele.Interface}" + (p.EnPorte ? ", en porte" : ", pupitre machine"),
                    Fabricant = modele.Fabricant, Reference = modele.Reference,
                    LargeurMm = modele.Largeur, HauteurMm = modele.Hauteur, ProfondeurMm = modele.Profondeur,
                    PertesW = 24 * modele.Consommation, EnPorte = p.EnPorte, HorsArmoire = !p.EnPorte,
                });
            }
        }
    }

    static List<ConsommationCommande> Estimation(ConfigurationAutomate c)
    {
        var reserve = 1 + c.ReservePourcent / 100;
        var cartes = CatalogueCartesEs.TypesVoies.Sum(t => (int)Math.Ceiling(Requises(Voies(c, t), reserve) / (t[0] == 'D' ? 16.0 : 8.0)));
        var liste = new List<ConsommationCommande> { new("CPU automate (estimation)", ConsommationCpu) };
        if (cartes > 0) liste.Add(new($"Cartes d'E/S × {cartes} (estimation, hors capteurs)", cartes * ConsommationCarte));
        return liste;
    }

    static ElementMateriel Element(string repere, string famille, CarteEs c, string marque, string calibre) => new()
    {
        Repere = repere, Famille = famille, Designation = c.Designation, Calibre = calibre, Fabricant = marque, Reference = c.Reference,
        LargeurMm = c.Largeur, HauteurMm = c.Hauteur, ProfondeurMm = c.Profondeur, PertesW = c.Pertes, Loge = c.Loge,
    };

    static ElementMateriel Accessoire(string repere, string reference, string designation, string marque) => new()
    {
        Repere = repere, Famille = "Accessoire automate", Designation = designation, Calibre = "accessoire",
        Fabricant = marque, Reference = reference, Loge = true,
    };

    static string Liste(IEnumerable<CarteEs> cartes) =>
        string.Join(" + ", cartes.GroupBy(c => c.Reference).Select(g => $"{g.Count()} × {g.First().Designation} ({g.Key})"));

    static string Capacite(int? valeur) => valeur?.ToString() ?? "?";
}
