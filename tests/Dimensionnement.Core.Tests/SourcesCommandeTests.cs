using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class SourcesCommandeTests
{
    [Fact]
    public void Designation_suit_le_primaire_et_la_sortie_tant_qu_elle_n_est_pas_saisie()
    {
        var a = new DepartAlimentation24V();
        Assert.Equal("Alimentation 230 V AC / 24 V DC", a.Designation);
        a.Alimentation = TypeAlimentation.Bi400;
        Assert.Equal("Alimentation 400 V AC (2 phases) / 24 V DC", a.Designation);
        a.Source = SourceAlimentation.Transformateur;
        a.Sortie = TensionCommande.Alternatif24;
        Assert.Equal("Transformateur 400/24 V AC", a.Designation);

        a.Designation = "Alim pupitre";
        a.Alimentation = TypeAlimentation.Mono230;
        Assert.Equal("Alim pupitre", a.Designation);
    }

    [Theory]
    [InlineData(TypeAlimentation.Mono230, SourceAlimentation.Decoupage, TensionCommande.Continu24, 2)]
    [InlineData(TypeAlimentation.Bi400, SourceAlimentation.Decoupage, TensionCommande.Continu24, 2)]
    [InlineData(TypeAlimentation.Tri400, SourceAlimentation.Decoupage, TensionCommande.Continu24, 3)]
    [InlineData(TypeAlimentation.Mono230, SourceAlimentation.Transformateur, TensionCommande.Alternatif24, 2)]
    [InlineData(TypeAlimentation.Bi400, SourceAlimentation.Transformateur, TensionCommande.Alternatif24, 2)]
    public void Protection_suit_le_primaire(TypeAlimentation primaire, SourceAlimentation source, TensionCommande sortie, int poles)
    {
        var projet = new Projet();
        projet.Ajouter(new DepartAlimentation24V { Alimentation = primaire, Source = source, Sortie = sortie });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.5 });
        var r = CalculProjet.Calculer(projet);
        var q = r.Arbre.Parcourir().First(e => e.Repere == $"Q1");
        Assert.Equal(poles, q.Poles);
    }

    [Fact]
    public void Catalogue_utilisateur_complete_des_nouveaux_modeles_livres()
    {
        var livre = CatalogueAutomates.Charger();
        var fichier = Path.GetTempFileName();
        File.WriteAllText(fichier, """[{"marque":"Siemens","gamme":"S7-1200","cpu":"CPU 1211C","reference":"6ES7211-1AE40-0XB0","memoireTravailKo":999}]""");
        try
        {
            var fusion = CatalogueAutomates.Charger(fichier);
            Assert.Equal(999, fusion.Single(c => c.Reference == "6ES7211-1AE40-0XB0").MemoireTravailKo);
            Assert.Equal(livre.Count, fusion.Count);
        }
        finally { File.Delete(fichier); }
    }
}

public class BorniersTests
{
    [Fact]
    public void Borniers_dans_leur_zone_en_bas_commande_a_gauche_puissance_a_droite()
    {
        var a = CalculProjet.Calculer(Version3Tests.ProjetMachine()).Armoire!;
        var x1 = a.Appareils.Single(x => x.Repere == "X1");
        var x2 = a.Appareils.Single(x => x.Repere == "X2");
        var autres = a.Appareils.Where(x => x.Famille != "Bornier").ToList();
        Assert.True(x2.X + x2.Largeur + Dimensionnement.Core.Armoire.CalculArmoire.SeparationBorniers <= x1.X + 0.5);
        Assert.All(autres, x => Assert.True(x.Y + x.Hauteur <= x1.Y));
        Assert.Contains(a.Bandes, b => b.Bornier);
        Assert.True(x1.Y + x1.Hauteur <= a.PlaqueY + a.PlaqueHauteur);
    }

    [Fact]
    public void Frein_a_alimentation_separee_ajoute_deux_bornes()
    {
        var projet = new Projet();
        var m = projet.Ajouter(new DepartMoteur { PuissanceKw = 0.75, Frein = true, AlimentationFrein = AlimentationFrein.Separee230 });
        var avec = Dimensionnement.Core.Armoire.CalculArmoire.BornesPuissance(CalculProjet.Calculer(projet)).Single().Nombre;
        m.Frein = false;
        var sans = Dimensionnement.Core.Armoire.CalculArmoire.BornesPuissance(CalculProjet.Calculer(projet)).Single().Nombre;
        Assert.Equal(sans + 2, avec);
    }
}

public class ExtensionArriveeTests
{
    [Fact]
    public void Coefficient_d_extension_grossit_le_cable_d_arrivee_et_donne_la_marge()
    {
        var projet = Version3Tests.ProjetMachine();
        var sans = CalculProjet.Calculer(projet).Arbre.Parcourir().Single(e => e.Repere == "W0").Calibre;
        projet.Reseau.CoefficientExtension = 2;
        var r = CalculProjet.Calculer(projet);
        Assert.NotEqual(sans, r.Arbre.Parcourir().Single(e => e.Repere == "W0").Calibre);
        Assert.Contains(r.NoteArrivee.Lignes, l => l.Libelle == "Marge en intensité");
    }
}

public class CommandeAlimenteeTests
{
    static Projet AvecAutomate()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2 });
        projet.Automate.Actif = true;
        projet.Automate.EntreesTor = 16;
        return projet;
    }

    [Fact]
    public void Automate_relie_a_son_alimentation_24_V()
    {
        var projet = AvecAutomate();
        projet.Ajouter(new DepartAlimentation24V());
        var carte = Dimensionnement.Core.Materiel.SyntheseMateriel.Grouper(CalculProjet.Calculer(projet))
            .Single(g => g.Titre == Dimensionnement.Core.Materiel.SyntheseMateriel.TitreCommande).Cartes[0];
        Assert.Equal("Alimentation", carte.Chaine[0].Element.Famille);
    }

    [Fact]
    public void Sans_alimentation_la_commande_pend_a_une_alimentation_a_ajouter()
    {
        var r = CalculProjet.Calculer(AvecAutomate());
        var carte = Dimensionnement.Core.Materiel.SyntheseMateriel.Grouper(r)
            .Single(g => g.Titre == Dimensionnement.Core.Materiel.SyntheseMateriel.TitreCommande).Cartes[0];
        Assert.Equal(CalculProjet.SourceManquante, carte.Chaine[0].Element.Famille);
        Assert.Equal(StatutCalcul.NonConforme, carte.Statut);
        Assert.DoesNotContain(Dimensionnement.Core.Materiel.ListeDevis.Generer(r), l => l.Designation.Contains("à ajouter"));
    }

    [Fact]
    public void Seul_le_materiel_indicatif_passe_en_orange()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());
        var g = r.Arbre.Parcourir().First(e => e.Famille == "Alimentation" && e.Reference.Length > 0);
        Dimensionnement.Core.Materiel.SyntheseMateriel.MarquerAVerifier(r, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { g.Reference });
        Assert.Equal(StatutCalcul.Alerte, g.Statut);
        Assert.All(r.Arbre.Parcourir().Where(e => !e.AVerifier && e.Statut != StatutCalcul.NonConforme), e => Assert.Equal(StatutCalcul.Conforme, e.Statut));
    }
}
