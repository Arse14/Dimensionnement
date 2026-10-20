using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Tests;

public class CalculDepartTests
{
    static readonly Reseau Reseau = new();

    static DepartMoteur Moteur75(ModeDemarrage demarrage = ModeDemarrage.Direct, double longueur = 10) => new()
    {
        Numero = 1, PuissanceKw = 7.5, CosPhi = 0.84, Rendement = 0.89, RapportId = 7.5,
        Demarrage = demarrage, Cable = new CaracteristiquesCable { Longueur = longueur, Pose = MethodePose.C },
    };

    [Fact]
    public void Moteur_7_5_kW_direct_donne_disjoncteur_13_18_et_contacteur_18_A()
    {
        var r = CalculDepart.Calculer(Moteur75(), Reseau);

        Assert.Equal(14.48, r.CourantEmploi, 2);
        var q = r.Materiel;
        Assert.Equal("Q1", q.Repere);
        Assert.StartsWith("13–18 A", q.Calibre);
        Assert.Equal("AC-3 18 A", q.Enfants.Single().Calibre);
        Assert.Equal(1.5, r.Section);
        Assert.DoesNotContain(r.Note.Lignes, l => l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Grande_longueur_augmente_la_section_pour_la_chute_de_tension()
    {
        var r = CalculDepart.Calculer(Moteur75(longueur: 100), Reseau);

        // 1,5 mm² donnerait ≈ 7,9 % ; 2,5 mm² donne ≈ 4,8 % < 5 %.
        Assert.Equal(2.5, r.Section);
        Assert.InRange(r.ChuteTension, 4.7, 4.9);
    }

    [Fact]
    public void Section_forcee_trop_faible_est_signalee_non_conforme()
    {
        var m = Moteur75(longueur: 100);
        m.Forcages[CodesCalcul.Section] = new Forcage { Valeur = 1.5, Motif = "câble existant" };

        var r = CalculDepart.Calculer(m, Reseau);

        Assert.Equal(1.5, r.Section);
        Assert.Contains(r.Note.Lignes, l => l.Code == CodesCalcul.Section && l.Statut == StatutCalcul.Forcee);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Chute de tension" && l.Statut == StatutCalcul.NonConforme);
        Assert.Equal(StatutCalcul.NonConforme, r.Note.StatutGlobal);
    }

    [Fact]
    public void Etoile_triangle_dimensionne_les_contacteurs_sur_In_racine_de_3_et_In_sur_3()
    {
        var m = new DepartMoteur { Numero = 2, PuissanceKw = 30, CourantPlaque = 57, CosPhi = 0.86, Rendement = 0.92,
            Demarrage = ModeDemarrage.EtoileTriangle };

        var r = CalculDepart.Calculer(m, Reseau);

        var ligne = r.Materiel.Enfants.Single();
        Assert.Equal("KM2.1", ligne.Repere);
        Assert.Equal("AC-3 38 A", ligne.Calibre);   // 57 / √3 = 32,9 A
        Assert.Equal("AC-3 25 A", ligne.Enfants.Single(e => e.Repere == "KM2.2").Calibre); // 57 / 3 = 19 A
    }

    [Fact]
    public void Ecart_avec_la_plaque_superieur_a_10_pourcent_donne_une_alerte()
    {
        var m = Moteur75();
        m.CourantPlaque = 20;

        var r = CalculDepart.Calculer(m, Reseau);

        Assert.Equal(20, r.CourantEmploi);
        Assert.Contains(r.Note.Lignes, l => l.Libelle == "Cohérence avec la plaque" && l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void Variateur_preconise_un_differentiel_type_B_en_triphase()
    {
        var m = Moteur75(ModeDemarrage.Variateur);
        m.Differentiel = SensibiliteDifferentiel.Ma300;

        var r = CalculDepart.Calculer(m, Reseau);

        Assert.Equal(16, r.CalibreProtection);
        Assert.Contains(r.Note.Lignes, l => l.Message.StartsWith("300 mA type B"));
        Assert.Equal("U1", r.Materiel.Enfants.Single().Repere);
    }

    [Fact]
    public void Resistance_9_kW_triphasee_donne_13_A_et_disjoncteur_13_A()
    {
        var r = CalculDepart.Calculer(new DepartResistif { Numero = 3, PuissanceKw = 9 }, Reseau);

        Assert.Equal(12.99, r.CourantEmploi, 2);
        Assert.Equal(13, r.CalibreProtection);
        Assert.Equal(1.5, r.Section);
    }

    [Fact]
    public void Prise_sans_differentiel_30_mA_est_non_conforme()
    {
        var p = new DepartPrise { Numero = 4, Differentiel = SensibiliteDifferentiel.Aucun };

        var r = CalculDepart.Calculer(p, Reseau);

        Assert.Equal(StatutCalcul.NonConforme, r.Note.StatutGlobal);
    }

    [Theory]
    [InlineData(TypeAlimentation.Tetra400, SchemaLiaisonTerre.TT, "3P+N")]
    [InlineData(TypeAlimentation.Tetra400, SchemaLiaisonTerre.TNC, "3P (PEN jamais coupé)")]
    [InlineData(TypeAlimentation.Mono230, SchemaLiaisonTerre.TT, "1P+N")]
    [InlineData(TypeAlimentation.Tri400, SchemaLiaisonTerre.IT, "3P")]
    public void Poles_selon_alimentation_et_slt(TypeAlimentation a, SchemaLiaisonTerre slt, string attendu) =>
        Assert.Equal(attendu, CalculDepart.Poles(a, slt));
}
