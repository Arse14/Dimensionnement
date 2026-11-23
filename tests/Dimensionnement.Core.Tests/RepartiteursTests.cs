using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class RepartiteursTests
{
    static readonly ModeleRepartiteur[] Catalogue =
    {
        new() { Fabricant = "Siemens", Gamme = "5ST", Reference = "S-4P-100-6", Poles = 4, Courant = 100, Sorties = 6, SectionMaxSortie = 16, Largeur = 72, Hauteur = 90, Profondeur = 60 },
        new() { Fabricant = "Siemens", Gamme = "5ST", Reference = "S-4P-160-12", Poles = 4, Courant = 160, Sorties = 12, SectionMaxSortie = 35, Largeur = 108, Hauteur = 100, Profondeur = 70 },
        new() { Fabricant = "Siemens", Gamme = "8WH", Reference = "S-1P-250-15", Poles = 1, Courant = 250, Sorties = 15, SectionMaxSortie = 35, Largeur = 27, Hauteur = 120, Profondeur = 70 },
        new() { Fabricant = "Schneider", Gamme = "Linergy DS", Reference = "LGY-4P-125-7", Poles = 4, Courant = 125, Sorties = 7, SectionMaxSortie = 16, Largeur = 72, Hauteur = 90, Profondeur = 60 },
    };

    [Fact]
    public void Plus_petit_bloc_qui_porte_le_courant_et_les_sorties()
    {
        Assert.Equal("S-4P-100-6", CatalogueRepartiteurs.Choisir("Siemens", 4, 63, 6, 10, Catalogue)!.Reference);
        // 8 sorties : le 6 sorties ne suffit plus
        Assert.Equal("S-4P-160-12", CatalogueRepartiteurs.Choisir("Siemens", 4, 63, 8, 10, Catalogue)!.Reference);
        // 25 mm² en départ : il faut le bloc qui accepte 35 mm²
        Assert.Equal("S-4P-160-12", CatalogueRepartiteurs.Choisir("Siemens", 3, 80, 4, 25, Catalogue)!.Reference);
    }

    [Fact]
    public void Au_dela_des_blocs_multipolaires_un_bloc_unipolaire_par_pole()
    {
        Assert.Equal("S-1P-250-15", CatalogueRepartiteurs.Choisir("Siemens", 4, 200, 10, 16, Catalogue)!.Reference);
    }

    [Fact]
    public void La_marque_est_respectee()
    {
        Assert.Equal("LGY-4P-125-7", CatalogueRepartiteurs.Choisir("Schneider", 4, 100, 5, 10, Catalogue)!.Reference);
        Assert.Null(CatalogueRepartiteurs.Choisir("Schneider", 4, 200, 5, 10, Catalogue));
    }

    [Fact]
    public void Repartiteur_dans_la_nomenclature_mais_pas_dans_la_carte_d_arrivee()
    {
        var projet = new Projet();
        for (var i = 0; i < 5; i++) projet.Ajouter(new DepartMoteur { Designation = $"Moteur {i}", PuissanceKw = 2.2 });

        var r = CalculProjet.Calculer(projet);
        var xr = Assert.Single(r.Nomenclature, e => e.Famille == "Répartiteur");
        Assert.Contains("6 sorties par pôle", xr.Calibre); // 5 départs + 20 %

        projet.Reseau.Repartiteur = false;
        Assert.DoesNotContain(CalculProjet.Calculer(projet).Nomenclature, e => e.Famille == "Répartiteur");
    }
}
