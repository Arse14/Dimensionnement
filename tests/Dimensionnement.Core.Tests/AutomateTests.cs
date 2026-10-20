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

    [Fact]
    public void Peu_de_peripheriques_donne_la_plus_petite_CPU_avec_alerte_catalogue_non_verifie()
    {
        var r = CalculAutomate.Calculer(Besoin(10), Catalogue);

        // 10 périphériques + 20 % = 12 ≤ 16
        Assert.Equal("CPU 1211C", r.Cpu!.Cpu);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Catalogue vérifié" && l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void Charge_de_communication_elevee_passe_en_S7_1500()
    {
        var r = CalculAutomate.Calculer(Besoin(20), Catalogue);

        // 20 + 20 % = 24 > 16 : les S7-1200 ne suffisent plus
        Assert.Equal("CPU 1511-1 PN", r.Cpu!.Cpu);
    }

    [Fact]
    public void Securite_integree_ecarte_les_CPU_sans_version_F()
    {
        var r = CalculAutomate.Calculer(Besoin(5, securite: true), Catalogue);

        Assert.Equal("CPU 1212C", r.Cpu!.Cpu);
        Assert.EndsWith("F", r.Appareils.Single().Designation);
    }

    [Fact]
    public void Cartes_d_ES_avec_reserve()
    {
        var r = CalculAutomate.Calculer(Besoin(2), Catalogue);

        // 40 E × 1,2 = 48 → 3 cartes ; 20 S × 1,2 = 24 → 2 cartes ; 4 EA × 1,2 = 4,8 → 1 carte
        var cpu = r.Appareils.Single();
        Assert.Equal(5, cpu.Enfants.Single(e => e.Repere == "API1-TOR").Quantite);
        Assert.Equal(1, cpu.Enfants.Single(e => e.Repere == "API1-ANA").Quantite);
    }

    [Fact]
    public void Catalogue_personnalise_verifie_supprime_les_alertes()
    {
        var catalogue = CatalogueAutomates.Lire("""
            [{ "marque": "Schneider", "gamme": "Modicon M251", "cpu": "M251", "reference": "TM251MESE",
               "peripheriquesReseauMax": 64, "connexionsMax": 32, "opcUa": false, "verifie": true, "source": "fiche technique" }]
            """);
        var besoin = Besoin(10);
        besoin.Marque = MarqueAutomate.Schneider;

        var r = CalculAutomate.Calculer(besoin, catalogue);

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

        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith("CPU automate"));
        var alimentation = r.Arbre.Parcourir().Single(e => e.Famille == "Alimentation");
        Assert.Contains(alimentation.Enfants, e => e.Repere == "API1");
    }
}
