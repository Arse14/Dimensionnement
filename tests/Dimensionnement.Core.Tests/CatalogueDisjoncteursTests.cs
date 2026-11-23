using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

/// <summary>Catalogue des disjoncteurs par référence (relevés du 21/10/2026).</summary>
public class CatalogueDisjoncteursTests
{
    [Fact]
    public void Catalogue_embarque_couvre_les_quatre_marques_avec_des_cotes()
    {
        var c = CatalogueDisjoncteurs.Charger();
        foreach (var marque in new[] { "Siemens", "Schneider", "ABB", "Legrand" })
            Assert.Contains(c, m => m.Fabricant == marque);
        Assert.All(c, m => Assert.True(m.Largeur > 0 && m.Hauteur > 0 && m.Calibre > 0, m.Reference));
        Assert.Equal(c.Count, c.Select(m => m.Reference).Distinct().Count());
    }

    [Fact]
    public void Reference_de_la_capture_reconnue_avec_ses_cotes()
    {
        var m = CatalogueDisjoncteurs.Trouver("5SL6106-7");
        Assert.NotNull(m);
        Assert.Equal(("C", 1, 6.0), (m!.Courbe, m.Poles, m.Calibre));
        Assert.Equal((18.0, 90.0, 76.0), (m.Largeur, m.Hauteur, m.Profondeur));
        Assert.True(m.Verifie);
    }

    [Fact]
    public void Un_P_plus_N_prefere_l_appareil_a_neutre()
    {
        var m = CatalogueDisjoncteurs.Choisir("Siemens", "C", 2, 6, polesTexte: "1P+N");
        Assert.Equal("5SL6506-7", m!.Reference);
        Assert.Equal("5SL6206-7", CatalogueDisjoncteurs.Choisir("Siemens", "C", 2, 6)!.Reference);
    }

    [Fact]
    public void Icc_eleve_fait_monter_de_gamme()
    {
        Assert.StartsWith("5SL6", CatalogueDisjoncteurs.Choisir("Siemens", "C", 3, 16, 6)!.Reference);
        var fort = CatalogueDisjoncteurs.Choisir("Siemens", "C", 3, 16, 25)!;
        Assert.True(fort.Icu >= 25, fort.Reference);
    }

    [Fact]
    public void Le_calcul_reporte_reference_cotes_et_pertes_sur_le_disjoncteur()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Reseau.IccKiloAmperes = 6;
        projet.References.Fabricant = FabricantMateriel.Siemens;
        var r = CalculProjet.Calculer(projet);
        var q = r.Arbre.Parcourir().Where(e => e.Famille == "Disjoncteur" && e.Courant <= 63).ToList();
        Assert.NotEmpty(q);
        Assert.All(q, e =>
        {
            Assert.StartsWith("5SL6", e.Reference);
            Assert.Equal(90, e.HauteurMm);
            Assert.True(e.LargeurMm >= 18 && e.PertesW > 0, e.Repere);
        });
    }

    [Fact]
    public void Calibre_absent_du_catalogue_garde_le_repli_par_gamme()
    {
        var e = new ElementMateriel { Repere = "Q9", Famille = "Disjoncteur", Courant = 1000, Poles = 3, Courbe = "C", Calibre = "1000 A" };
        var arbre = new ElementMateriel { Repere = "R", Famille = "Réseau" }.Ajouter(e);
        CatalogueReferences.Appliquer(arbre, new ConfigurationReferences(), CatalogueReferences.Charger());
        Assert.DoesNotContain(CatalogueDisjoncteurs.Actif, m => m.Reference == e.Reference);
    }
}
