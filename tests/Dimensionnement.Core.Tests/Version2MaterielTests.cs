using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class Version2MaterielTests
{
    static readonly Reseau Reseau = new();

    [Fact]
    public void Moteur_sur_fusibles_aM_avec_relais_thermique()
    {
        var m = new DepartMoteur { Numero = 1, PuissanceKw = 7.5, CosPhi = 0.84, Rendement = 0.89,
            Protection = TypeProtection.Fusibles };

        var r = CalculDepart.Calculer(m, Reseau);

        Assert.Equal(16, r.CalibreProtection);
        var elements = r.Materiel.Parcourir().ToList();
        Assert.Equal("Sectionneur porte-fusibles 10×38", elements[0].Designation);
        Assert.Contains(elements, e => e.Repere == "FU1" && e.Calibre == "16 A" && e.Quantite == 3);
        Assert.Contains(elements, e => e.Repere == "KM1");
        Assert.Contains(elements, e => e.Repere == "F1" && e.Famille == "Relais thermique");
        Assert.Equal(1.5, r.Section);
    }

    [Fact]
    public void Fusibles_gG_imposent_le_coefficient_k3_sur_le_cable()
    {
        var r = CalculDepart.Calculer(new DepartResistif { Numero = 2, PuissanceKw = 9, Protection = TypeProtection.Fusibles }, Reseau);

        // Ib = 13 A → gG 16 A ; Iz ≥ 1,21 × 16 = 19,4 A : 1,5 mm² (17,5 A) ne suffit plus, 2,5 mm² (24 A) convient.
        Assert.Equal(16, r.CalibreProtection);
        Assert.Equal(2.5, r.Section);
    }

    [Fact]
    public void Demarreur_progressif_entre_le_disjoncteur_moteur_et_le_cable()
    {
        var m = new DepartMoteur { Numero = 3, PuissanceKw = 11, CourantPlaque = 21, Demarrage = ModeDemarrage.DemarreurProgressif };

        var r = CalculDepart.Calculer(m, Reseau);

        var u = r.Materiel.Enfants.Single();
        Assert.Equal("U3", u.Repere);
        Assert.Equal("Démarreur progressif", u.Famille);
        Assert.Equal("W3", u.Enfants.Single().Repere);
        Assert.DoesNotContain(r.Note.Lignes, l => l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Alimentation_24V_dimensionnee_sur_le_bilan_avec_25_pourcent_de_reserve()
    {
        var projet = new Projet();
        projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Électrovanne", CourantMa = 500, Quantite = 2 });
        projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Pupitre", CourantMa = 3000 });
        projet.Ajouter(new DepartAlimentation24V());

        var r = CalculProjet.Calculer(projet);

        Assert.Equal(4, r.Consommation24V, 3);
        var alim = r.Departs.Single();
        Assert.Contains(alim.Note.Lignes, l => l.Code == CodesCalcul.CalibreAlimentation24V && l.ValeurRetenue == 5);
        // 24 V × 5 A / 0,9 = 133 W ; 133 / (230 × 0,95) = 0,61 A
        Assert.Equal(0.61, alim.CourantEmploi, 2);
    }

    [Fact]
    public void Consommation_24V_sans_alimentation_donne_une_alerte()
    {
        var projet = new Projet();
        projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Capteurs", CourantMa = 200 });

        var r = CalculProjet.Calculer(projet);

        Assert.Contains(r.NoteArrivee.Lignes, l => l.Libelle == "Alimentation 24 V" && l.Statut == StatutCalcul.Alerte);
    }

    [Theory]
    [InlineData(32, ClasseFusible.gG, "10×38")]
    [InlineData(32, ClasseFusible.aM, "14×51")]
    [InlineData(125, ClasseFusible.gG, "22×58")]
    [InlineData(125, ClasseFusible.aM, "NH00")]
    [InlineData(250, ClasseFusible.gG, "NH1")]
    public void Taille_des_fusibles(double calibre, ClasseFusible classe, string taille) =>
        Assert.Equal(taille, Calibres.TailleFusible(calibre, classe));
}
