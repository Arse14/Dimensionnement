using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class AutomateTests
{
    static readonly IReadOnlyList<CpuAutomate> Catalogue = CatalogueAutomates.Charger();

    static ConfigurationAutomate Besoin(int peripheriques, bool securite = false) => new()
    {
        Actif = true, Marque = MarqueAutomate.Siemens, PeripheriquesReseau = peripheriques, Ihm = 1,
        EntreesTor = 40, SortiesTor = 20, EntreesAnalogiques = 4, SecuriteIntegree = securite,
    };

    [Fact]
    public void Catalogue_embarque_couvre_Siemens_et_Schneider()
    {
        Assert.Contains(Catalogue, c => c.Marque == "Siemens" && c.Gamme == "S7-1500");
        Assert.Contains(Catalogue, c => c.Marque == "Schneider" && c.Gamme == "Modicon M580");
    }

    static ConfigurationAutomate SansEs(int peripheriques, bool securite = false) => new()
    {
        Actif = true, Marque = MarqueAutomate.Siemens, PeripheriquesReseau = peripheriques, Ihm = 1, SecuriteIntegree = securite,
    };

    [Fact]
    public void Peu_de_peripheriques_sans_cartes_donne_la_plus_petite_CPU_avec_alerte_catalogue_non_verifie()
    {
        var r = CalculAutomate.Calculer(SansEs(10), Catalogue);

        // 10 périphériques + 20 % = 12 ≤ 16
        Assert.Equal("CPU 1211C", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Catalogue vérifié" && l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void La_CPU_monte_en_gamme_tant_que_ses_cartes_ne_tiennent_pas()
    {
        var r = CalculAutomate.Calculer(Besoin(2), Catalogue);

        // 1211C : aucune carte admise ; 1212C : 2 cartes ; 1214C : 8 cartes, il en faut 5.
        Assert.Equal("CPU 1214C", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Cartes d'E/S admises" && l.Message.StartsWith("5 carte(s) pour 8"));
    }

    [Fact]
    public void Charge_de_communication_elevee_passe_en_S7_1500()
    {
        var r = CalculAutomate.Calculer(Besoin(20), Catalogue);

        // 20 + 20 % = 24 > 16 : les S7-1200 ne suffisent plus
        Assert.Equal("CPU 1511-1 PN", r.Cpu!.Cpu);
        Assert.All(r.Plan!.Cartes, c => Assert.Equal("S7-1500", c.Famille));
    }

    [Fact]
    public void Securite_integree_ecarte_les_CPU_sans_version_F()
    {
        var r = CalculAutomate.Calculer(SansEs(5, securite: true), Catalogue);

        Assert.Equal("CPU 1212C", r.Cpu!.Cpu);
        Assert.EndsWith("F", r.Appareils.Single().Designation);
    }

    [Fact]
    public void Cartes_standard_ajoutees_au_fur_et_a_mesure_avec_reserve_et_voies_integrees()
    {
        var r = CalculAutomate.Calculer(Besoin(2), Catalogue);

        // DI : 40 × 1,2 = 48 − 14 intégrées = 34 → 16 + 16 + 8 ; DQ : 24 − 10 = 14 → 16 ; AI : 5 − 2 = 3 → 4.
        var cpu = r.Appareils.Single();
        var cartes = cpu.Enfants.Select(e => e.Reference).ToList();
        Assert.Equal(new[] { "6ES7221-1BH32-0XB0", "6ES7221-1BH32-0XB0", "6ES7221-1BF32-0XB0", "6ES7222-1BH32-0XB0", "6ES7231-4HD32-0XB0" }, cartes);
        Assert.Equal(new[] { "API1.1", "API1.2", "API1.3", "API1.4", "API1.5" }, cpu.Enfants.Select(e => e.Repere));
        Assert.All(cpu.Enfants, e => Assert.Equal(45, e.LargeurMm));
        Assert.Equal(54, r.Plan!.Besoins.Single(b => b.Type == "DI").Disponibles);
    }

    [Fact]
    public void Cartes_compactes_Siemens_retiennent_une_CPU_ET200SP_et_ses_BaseUnit()
    {
        var besoin = Besoin(2);
        besoin.CartesCompactes = true;

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        Assert.Equal("CPU 1510SP-1 PN", r.Cpu!.Cpu);
        Assert.False(r.Plan!.Station);
        var cartes = r.Appareils.Single().Enfants;
        Assert.All(cartes, c => Assert.Equal(15, c.LargeurMm));
        // La première carte reçoit la BaseUnit claire (nouveau groupe de potentiel), les suivantes la foncée.
        Assert.Equal("6ES7193-6BP00-0DA0", cartes[0].Enfants.Single().Reference);
        Assert.Equal("6ES7193-6BP00-0BA0", cartes[1].Enfants.Single().Reference);
        Assert.True(cartes[0].Enfants.Single().Loge);
    }

    [Fact]
    public void CPU_imposee_impose_la_famille_de_ses_cartes()
    {
        var besoin = Besoin(2);
        besoin.CpuImposee = "6ES7513-1AL02-0AB0";

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        Assert.Equal("CPU 1513-1 PN", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "CPU imposée");
        var cartes = r.Appareils.Single().Enfants;
        Assert.All(cartes, c => Assert.StartsWith("6ES75", c.Reference));
        // DI : 48 voies → 32 + 16 ; un connecteur frontal par carte.
        Assert.Equal(new[] { "6ES7521-1BL00-0AB0", "6ES7521-1BH10-0AA0" }, cartes.Take(2).Select(c => c.Reference));
        Assert.All(cartes, c => Assert.Equal("6ES7592-1AM00-0XB0", c.Enfants.Single().Reference));
    }

    [Fact]
    public void CPU_imposee_trop_petite_est_non_conforme()
    {
        var besoin = Besoin(2);
        besoin.CpuImposee = "6ES7212-1AE40-0XB0";

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        Assert.Equal("CPU 1212C", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Cartes d'E/S admises" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void CPU_classique_avec_cartes_compactes_ajoute_une_station_ET200SP()
    {
        var besoin = Besoin(2);
        besoin.CpuImposee = "6ES7212-1AE40-0XB0";
        besoin.CartesCompactes = true;

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        Assert.True(r.Plan!.Station);
        var station = r.Appareils.Single().Enfants.Single(e => e.Repere == "API1-ET");
        Assert.Equal("6ES7155-6AU01-0BN0", station.Reference);
        Assert.Contains(station.Enfants, e => e.Repere == "API1-ET.1");
        Assert.DoesNotContain(r.Note.Lignes, l => l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void M580_loge_ses_modules_dans_un_chassis()
    {
        var besoin = Besoin(2);
        besoin.CpuImposee = "BMEP581020";

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        var cpu = r.Appareils.Single();
        Assert.True(cpu.Loge);
        Assert.Equal("BMEXBP0800", cpu.Enfants.Single(e => e.Repere == "API1-CH").Reference);
        Assert.Contains(cpu.Enfants, e => e.Reference == "BMXCPS3020");
        Assert.All(cpu.Enfants.Where(e => e.Repere.StartsWith("API1.")), e => Assert.True(e.Loge));
    }

    [Fact]
    public void Stations_ET200SP_deportees_dans_l_arborescence_et_comptees_en_communication()
    {
        var besoin = SansEs(0);
        for (var i = 1; i <= 14; i++)
            besoin.Stations.Add(new StationDeportee { Nom = $"Îlot {i}", EntreesTor = 16, SortiesTor = 8, DansArmoire = i > 1 });

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        // 14 stations × 1,2 = 17 périphériques > 16 : les S7-1200 ne suffisent plus.
        Assert.Equal("S7-1500", r.Cpu!.Gamme);
        var cpu = r.Appareils.Single();
        var station = cpu.Enfants.Single(e => e.Repere == "API1-S1");
        Assert.Equal("6ES7155-6AU01-0BN0", station.Reference);
        // DI : 16 × 1,2 = 20 → 16 + 8 ; DQ : 10 → 16.
        Assert.Equal(3, station.Enfants.Count(e => e.Famille == "Entrées/sorties"));
        Assert.True(station.HorsArmoire);
        Assert.False(cpu.Enfants.Single(e => e.Repere == "API1-S2").HorsArmoire);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Station Îlot 1" && l.Statut == StatutCalcul.Conforme);
    }

    [Fact]
    public void Zone_d_adresses_d_ES_depassee_fait_monter_la_CPU()
    {
        var besoin = SansEs(0);
        for (var i = 1; i <= 4; i++)
            besoin.Stations.Add(new StationDeportee { Nom = $"Mesures {i}", EntreesAnalogiques = 120 });

        var r = CalculAutomate.Calculer(besoin, Catalogue);

        // 4 × 144 voies analogiques × 2 octets = 1 152 octets > 1 024 d'un S7-1200.
        Assert.Equal("CPU 1511-1 PN", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Zone d'adresses d'E/S" && l.Message.StartsWith("1152 octets"));
    }

    [Fact]
    public void Station_hors_armoire_absente_de_la_plaque()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Automate.Stations.Add(new StationDeportee { Nom = "Coffret machine", EntreesTor = 8, DansArmoire = false });
        projet.Automate.Stations.Add(new StationDeportee { Nom = "Îlot armoire", EntreesTor = 8 });

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.DoesNotContain(a.Appareils, x => x.Repere.StartsWith("API1-S1"));
        Assert.Contains(a.Appareils, x => x.Repere == "API1-S2");
    }

    [Fact]
    public void Catalogue_personnalise_verifie_supprime_les_alertes()
    {
        var catalogue = CatalogueAutomates.Lire("""
            [{ "marque": "Schneider", "gamme": "Modicon M251", "cpu": "M251", "reference": "TM251MESE", "cartesMax": 14,
               "peripheriquesReseauMax": 64, "connexionsMax": 32, "opcUa": false, "verifie": true, "source": "fiche technique" }]
            """);
        var cartes = CatalogueCartesEs.Lire("""
            [{ "famille": "Modicon TM3", "type": "DI", "reference": "TM3DI16", "voies": 16, "largeur": 27.4, "hauteur": 90, "verifie": true },
             { "famille": "Modicon TM3", "type": "DQ", "reference": "TM3DQ16T", "voies": 16, "largeur": 27.4, "hauteur": 90, "verifie": true },
             { "famille": "Modicon TM3", "type": "AI", "reference": "TM3AI8", "voies": 8, "largeur": 23.6, "hauteur": 90, "verifie": true }]
            """);
        var besoin = Besoin(10);
        besoin.Marque = MarqueAutomate.Schneider;

        var r = CalculAutomate.Calculer(besoin, catalogue, cartes);

        Assert.Equal("M251", r.Cpu!.Cpu);
        Assert.DoesNotContain(r.Note.Lignes, l => l.Statut >= StatutCalcul.Alerte);
    }

    [Fact]
    public void Besoin_trop_grand_est_non_conforme()
    {
        var r = CalculAutomate.Calculer(Besoin(2000), Catalogue);

        Assert.Null(r.Cpu);
        Assert.Equal(StatutCalcul.NonConforme, r.Note.StatutGlobal);
    }

    [Fact]
    public void Automate_rattache_au_24V_et_compte_dans_le_bilan()
    {
        var projet = new Projet { Automate = Besoin(4) };
        projet.Ajouter(new DepartAlimentation24V());

        var r = CalculProjet.Calculer(projet);

        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith("Automate CPU"));
        var alimentation = r.Arbre.Parcourir().Single(e => e.Famille == "Alimentation");
        Assert.Contains(alimentation.Enfants, e => e.Repere == "API1");
    }
}
