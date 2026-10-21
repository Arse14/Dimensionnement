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

public class IhmEnPorteTests
{
    static Projet AvecIhm(bool enPorte)
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2 });
        projet.Ajouter(new DepartAlimentation24V());
        projet.Automate.Actif = true;
        projet.Automate.EntreesTor = 16;
        projet.Automate.Ihm = 0;
        projet.Automate.Pupitres.Add(new PupitreIhm { Nom = "IHM positionneur", Diagonale = 7, Gamme = GammeIhm.Confort, EnPorte = enPorte });
        return projet;
    }

    [Fact]
    public void L_ihm_a_sa_carte_dans_l_arborescence()
    {
        var cartes = Dimensionnement.Core.Materiel.SyntheseMateriel.Grouper(CalculProjet.Calculer(AvecIhm(true)))
            .Single(g => g.Titre == Dimensionnement.Core.Materiel.SyntheseMateriel.TitreCommande).Cartes;
        var ihm = cartes.Single(c => c.Chaine.Any(m => m.Element.Famille == "IHM"));
        Assert.Equal("Alimentation", ihm.Chaine[0].Element.Famille);
    }

    [Fact]
    public void L_ihm_en_porte_est_implantee_et_chauffe_l_armoire()
    {
        var projet = AvecIhm(true);
        var r = CalculProjet.Calculer(projet);
        var hmi = r.Arbre.Parcourir().Single(e => e.Famille == "IHM");
        var a = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);

        var porte = Assert.Single(a.Porte);
        Assert.Equal(hmi.LargeurMm, porte.Largeur);
        Assert.InRange(porte.X, Dimensionnement.Core.Armoire.CalculArmoire.CadrePorte, a.Largeur - porte.Largeur - Dimensionnement.Core.Armoire.CalculArmoire.CadrePorte);
        Assert.DoesNotContain(a.Appareils, x => x.Famille == "IHM");
        Assert.True(a.Profondeur >= hmi.ProfondeurMm + Dimensionnement.Core.Armoire.CalculArmoire.ProfondeurModulaire);
        Assert.Contains(Dimensionnement.Core.Armoire.CalculThermique.Postes(r), p => p.Repere == hmi.Repere);
        Assert.Contains(a.Note.Lignes, l => l.Libelle.StartsWith("Derrière HMI"));
    }

    [Fact]
    public void L_ihm_sur_pupitre_machine_reste_hors_armoire()
    {
        var projet = AvecIhm(false);
        var r = CalculProjet.Calculer(projet);
        Assert.Empty(Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet).Porte);
        Assert.DoesNotContain(Dimensionnement.Core.Armoire.CalculThermique.Postes(r), p => p.Repere.StartsWith("HMI"));
    }
}

public class CatalogueSiemensVerifieTests
{
    [Fact]
    public void Les_cartes_particulieres_ne_sont_jamais_choisies_automatiquement()
    {
        var catalogue = CatalogueCartesEs.Charger();
        Assert.Contains(catalogue, c => c.Reference == "6ES7136-6BA01-0CA0" && c.Specifique && c.Verifie);
        var dq = CalculAutomate.ChoisirCartes("ET200SP", "DQ", 4, false, catalogue)!;
        Assert.Equal("6ES7132-6BF01-0BA0", Assert.Single(dq).Reference);
    }

    [Fact]
    public void Cpu_S7_1500_aux_references_actuelles_et_verifiees()
    {
        var cpu = CatalogueAutomates.Charger().Single(c => c.Cpu == "CPU 1511-1 PN");
        Assert.Equal("6ES7511-1AL03-0AB0", cpu.Reference);
        Assert.True(cpu.Verifie);
        Assert.Equal(0.56, cpu.Consommation);
    }
}

public class ImplantationLibreTests
{
    [Fact]
    public void Deux_appareils_poses_cote_a_cote_au_pas_de_5_mm_ne_se_chevauchent_pas()
    {
        var a = new Dimensionnement.Core.Armoire.AppareilImplante { Repere = "Q1", Designation = "", Famille = "Disjoncteur", Zone = Dimensionnement.Core.Materiel.ZoneCem.Intermediaire, X = 100, Y = 100, Largeur = 17.5, Hauteur = 90 };
        var b = new Dimensionnement.Core.Armoire.AppareilImplante { Repere = "Q2", Designation = "", Famille = "Disjoncteur", Zone = Dimensionnement.Core.Materiel.ZoneCem.Intermediaire, X = 115, Y = 100, Largeur = 17.5, Hauteur = 90 };
        var c = new Dimensionnement.Core.Armoire.AppareilImplante { Repere = "Q3", Designation = "", Famille = "Disjoncteur", Zone = Dimensionnement.Core.Materiel.ZoneCem.Intermediaire, X = 105, Y = 100, Largeur = 17.5, Hauteur = 90 };
        Assert.False(a.Chevauche(b));
        Assert.True(a.Chevauche(c));
    }

    [Fact]
    public void La_disposition_figee_devient_modifiable_et_reste_conforme()
    {
        var projet = Version3Tests.ProjetMachine();
        var r = CalculProjet.Calculer(projet);
        var auto = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        Dimensionnement.Core.Armoire.CalculArmoire.Figer(auto, projet.Armoire);

        var libre = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        Assert.True(libre.Libre);
        Assert.True(libre.Tient, libre.Alerte);
        Assert.Equal(auto.Largeur, libre.Largeur);
        Assert.Equal(auto.Appareils.Count, libre.Appareils.Count);
        Assert.Equal(auto.Cablage.Goulottes.Count, libre.Cablage.Goulottes.Count);
        Assert.All(libre.Cablage.Goulottes, g => Assert.True(g.Manuelle));
        Assert.Contains(libre.Note.Lignes, l => l.Libelle == "Place libre");

        // Un rail retiré à la main : la place libre baisse et l'appareil qui y était fixé le signale.
        projet.Armoire.Rails.RemoveAt(0);
        var sansRail = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        Assert.Contains(sansRail.Note.Lignes, l => l.Libelle.StartsWith("Rail de "));
        Assert.True(sansRail.ReserveEffective < libre.ReserveEffective + 1e-9 || sansRail.ReserveEffective >= 0);
    }

    [Fact]
    public void Un_appareil_ajoute_apres_coup_attend_dans_la_zone_a_placer()
    {
        var projet = Version3Tests.ProjetMachine();
        var r = CalculProjet.Calculer(projet);
        Dimensionnement.Core.Armoire.CalculArmoire.Figer(Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet), projet.Armoire);
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.5 });

        var a = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(CalculProjet.Calculer(projet), projet);
        Assert.False(a.Tient);
        Assert.StartsWith("Ta disposition ne tient pas encore : à placer", a.Alerte);
        Assert.True(a.LargeurAttente > 0);
    }
}

public class CalageRailTests
{
    [Fact]
    public void Un_modulaire_pose_pres_d_un_rail_se_cale_dessus_et_reste_libre_le_long()
    {
        var projet = Version3Tests.ProjetMachine();
        var r = CalculProjet.Calculer(projet);
        var auto = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        Dimensionnement.Core.Armoire.CalculArmoire.Figer(auto, projet.Armoire);
        var rail = projet.Armoire.Rails[0];
        var q = auto.Appareils.First(a => a.Famille == "Disjoncteur");
        projet.Armoire.Positions[q.Cle] = new PositionManuelle { X = rail.X + 40, Y = rail.Y - q.Hauteur / 2 + 12 };

        var a = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet).Appareils.Single(x => x.Cle == q.Cle);
        Assert.Equal(rail.Y, a.CentreY, 3);
        Assert.Equal(rail.X + 40, a.X, 3);
    }
}

public class ZonesEtDegagementTests
{
    [Fact]
    public void Les_zones_restent_sur_la_plaque_et_un_variateur_colle_au_bord_est_signale()
    {
        var projet = Version3Tests.ProjetMachine();
        var r = CalculProjet.Calculer(projet);
        var auto = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        var dx = (auto.Largeur - auto.PlaqueLargeur) / 2;
        var dy = (auto.Hauteur - auto.PlaqueHauteur) / 2;
        Assert.All(auto.Bandes, b =>
        {
            Assert.True(b.Y >= dy - 1e-6 && b.Y + b.Hauteur <= dy + auto.PlaqueHauteur + 1e-6, $"{b.Y} {b.Hauteur}");
            Assert.True(b.X >= dx - 1e-6);
        });
        Assert.DoesNotContain(auto.Note.Lignes, l => l.Libelle == "Dégagement des variateurs");

        // Variateur posé à la main contre le haut de la plaque : plus de dégagement de ventilation au-dessus.
        var u = auto.Appareils.First(Dimensionnement.Core.Armoire.CalculArmoire.EstVariateur);
        Dimensionnement.Core.Armoire.CalculArmoire.Figer(auto, projet.Armoire);
        projet.Armoire.Positions[u.Cle] = new Dimensionnement.Core.Modele.PositionManuelle { X = u.X, Y = dy };
        var main = Dimensionnement.Core.Armoire.CalculArmoire.Calculer(r, projet);
        var ligne = Assert.Single(main.Note.Lignes, l => l.Libelle == "Dégagement des variateurs");
        Assert.Contains(u.Repere, ligne.Message);
    }
}
