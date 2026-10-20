using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Rapport;

namespace Dimensionnement.Core.Tests;

public class SecuriteTests
{
    static Projet ProjetAvecSecurite(NiveauPerformance plr, int fonctions = 1)
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { Designation = "Convoyeur", PuissanceKw = 7.5, CosPhi = 0.84, Rendement = 0.89 });
        projet.Ajouter(new DepartMoteur { Designation = "Pompe", PuissanceKw = 4, Demarrage = ModeDemarrage.Variateur });
        projet.Ajouter(new DepartAlimentation24V());
        for (var i = 0; i < fonctions; i++)
            projet.Securite.Fonctions.Add(new FonctionSecurite { Nom = $"AU {i + 1}", PLr = plr, Departs = { 1, 2 } });
        return projet;
    }

    [Theory]
    [InlineData(NiveauPerformance.a, "B")]
    [InlineData(NiveauPerformance.c, "2")]
    [InlineData(NiveauPerformance.d, "3")]
    [InlineData(NiveauPerformance.e, "4")]
    public void Categorie_conseillee_selon_le_PLr(NiveauPerformance plr, string categorie) =>
        Assert.Equal(categorie, CalculSecurite.Categorie(plr));

    [Fact]
    public void PLr_d_ajoute_un_contacteur_redondant_et_le_STO_du_variateur()
    {
        var r = CalculProjet.Calculer(ProjetAvecSecurite(NiveauPerformance.d));

        var km = r.Arbre.Parcourir().Single(e => e.Repere == "KM1");
        var redondant = km.Enfants.Single();
        Assert.Equal("KM1.S", redondant.Repere);
        Assert.Equal("W1", redondant.Enfants.Single().Repere);
        Assert.Contains(r.Departs[1].Note.Lignes, l => l.Message.Contains("STO"));
        Assert.Contains(r.Nomenclature, e => e.Repere == "KM1.S");
    }

    [Fact]
    public void PLr_c_garde_un_seul_contacteur()
    {
        var r = CalculProjet.Calculer(ProjetAvecSecurite(NiveauPerformance.c));

        Assert.DoesNotContain(r.Arbre.Parcourir(), e => e.Repere == "KM1.S");
    }

    [Fact]
    public void Relais_par_fonction_puis_automate_au_dela_de_trois_fonctions()
    {
        var deux = CalculProjet.Calculer(ProjetAvecSecurite(NiveauPerformance.d, 2));
        var trois = CalculProjet.Calculer(ProjetAvecSecurite(NiveauPerformance.d, 3));

        Assert.Equal(2, deux.Securite!.Appareils.Count(a => a.Designation.StartsWith("Relais de sécurité")));
        Assert.Equal("Automate de sécurité", trois.Securite!.Appareils.Single().Designation);
        // Les appareils de sécurité sont rattachés à l'alimentation 24 V qui les alimente.
        var alimentation = trois.Arbre.Parcourir().Single(e => e.Famille == "Alimentation");
        Assert.Contains(alimentation.Enfants, e => e.Repere == "KA-SEC");
        Assert.True(trois.Consommation24V > 0);
    }

    [Fact]
    public void Depart_inexistant_donne_une_alerte()
    {
        var projet = ProjetAvecSecurite(NiveauPerformance.d);
        projet.Securite.Fonctions[0].DepartsTexte = "D1, 9";

        var r = CalculProjet.Calculer(projet);

        Assert.Equal(new[] { 1, 9 }, projet.Securite.Fonctions[0].Departs);
        Assert.Contains(r.Securite!.Note.Lignes, l => l.Statut == StatutCalcul.Alerte && l.Libelle.Contains("D09"));
    }

    [Fact]
    public void Export_SISTEMA_une_ligne_par_fonction()
    {
        var csv = ExportSistema.Generer(CalculProjet.Calculer(ProjetAvecSecurite(NiveauPerformance.e, 2)));

        var lignes = csv.Trim().Split('\n');
        Assert.Equal(3, lignes.Length);
        Assert.Contains("D01 KM1 + KM1.S", lignes[1]);
        Assert.Contains("STO variateur U2", lignes[1]);
    }
}
