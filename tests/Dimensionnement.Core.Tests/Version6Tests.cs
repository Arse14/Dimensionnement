using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using Dimensionnement.Core.Persistance;

namespace Dimensionnement.Core.Tests;

public class Version6Tests
{
    static IEnumerable<ElementMateriel> Arbre(Projet p) => CalculProjet.Calculer(p).Arbre.Parcourir();

    [Fact]
    public void Servomoteur_choisit_un_servovariateur_sans_contacteur_avec_cable_codeur()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.5, Demarrage = ModeDemarrage.Servomoteur });
        var r = CalculProjet.Calculer(projet);

        var u = r.Arbre.Parcourir().Single(e => e.Famille == "Variateur");
        Assert.Equal("Servovariateur", u.Designation);
        Assert.DoesNotContain(r.Arbre.Parcourir(), e => e.Famille == "Contacteur");
        Assert.Contains(ListeDevis.Generer(r), l => l.Designation.Contains("codeur"));
    }

    [Theory]
    [InlineData(TypeDeuxVitesses.Dahlander, 3)]
    [InlineData(TypeDeuxVitesses.DeuxEnroulements, 2)]
    public void Moteur_deux_vitesses_compte_ses_contacteurs(TypeDeuxVitesses type, int contacteurs)
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, Demarrage = ModeDemarrage.DeuxVitesses, TypeDeuxVitesses = type });

        Assert.Equal(contacteurs, Arbre(projet).Count(e => e.Famille == "Contacteur"));
        Assert.Equal(contacteurs, CalculProjet.Contacteurs(projet));
    }

    [Fact]
    public void Frein_230V_ajoute_protection_et_contacteur_frein_24V_passe_au_bilan()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2, Frein = true, AlimentationFrein = AlimentationFrein.Separee230 });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2, Frein = true, AlimentationFrein = AlimentationFrein.Continu24, PuissanceFreinW = 48 });

        var arbre = Arbre(projet).ToList();
        Assert.Contains(arbre, e => e.Repere == "KM1.F");
        Assert.Contains(arbre, e => e.Repere == "Q1.F");
        Assert.Contains(arbre, e => e.Repere == "KA2.F");
        Assert.Contains(CalculProjet.Peripherie24V(projet), c => c.Designation.Contains("Frein") && Math.Abs(c.Courant - 2) < 1e-9);
    }

    [Fact]
    public void Coffret_deporte_applique_sa_simultaneite()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartCoffret { PuissanceKw = 10, Simultaneite = 0.5, CosPhi = 1, Alimentation = TypeAlimentation.Tri400 });
        var r = CalculProjet.Calculer(projet);

        // Ib = 5 kW / (√3 × 400) = 7,2 A
        Assert.Equal(7.2, r.Departs[0].CourantEmploi, 1);
        Assert.Contains(r.Arbre.Parcourir(), e => e.Repere == "AC1");
    }

    [Fact]
    public void Parafoudre_et_controle_de_phases_sous_le_sectionneur_general()
    {
        var projet = new Projet();
        projet.Reseau.Parafoudre = true;
        projet.Reseau.ControlePhases = true;
        projet.Ajouter(new DepartResistif { PuissanceKw = 3 });

        var qs0 = Arbre(projet).Single(e => e.Repere == "QS0");
        Assert.Contains(qs0.Enfants, e => e.Repere == "Q-PF" && e.Enfants.Any(f => f.Famille == "Parafoudre"));
        Assert.Contains(qs0.Enfants, e => e.Repere == "Q-KCP" && e.Enfants.Any(f => f.Famille == "Relais de contrôle"));
        var tous = qs0.Parcourir().ToList();
        Assert.StartsWith("SENTRON 5SD7", tous.Single(e => e.Famille == "Parafoudre").Reference);
        Assert.Equal("3UG4512-1AR20", tous.Single(e => e.Famille == "Relais de contrôle").Reference);
    }

    [Fact]
    public void Ilot_d_electrovannes_au_bilan_hors_plaque_et_compte_sur_le_reseau()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartAlimentation24V());
        projet.Automate.Actif = true;
        projet.PeripheriquesMachine.Add(new PeripheriqueMachine { Nom = "Îlot presse", Elements = 10, CourantMa = 40, Simultaneite = 0.5 });
        var r = CalculProjet.Calculer(projet);

        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith("Îlot presse") && Math.Abs(c.Courant - 0.2) < 1e-9);
        var yv = r.Arbre.Parcourir().Single(e => e.Repere == "YV1");
        Assert.True(yv.HorsArmoire);
        Assert.Equal(1, CalculAutomate.Peripheriques(projet.Automate, 1));
        Assert.Contains(r.Automate!.Note.Lignes, l => l.Message.Contains("îlot"));
    }

    [Fact]
    public void Renommage_remplace_repere_et_designation_et_garde_la_position()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4 });
        var km = Arbre(projet).First(e => e.Famille == "Contacteur");
        projet.Renommages[km.CleRenommage] = new Renommage { Repere = "KM-Pompe", Designation = "Contacteur pompe" };
        projet.Armoire.Positions[km.Repere] = new PositionManuelle { X = 100, Y = 100 };

        var r = CalculProjet.Calculer(projet);
        var renomme = r.Arbre.Parcourir().Single(e => e.Repere == "KM-Pompe");
        Assert.Equal("Contacteur pompe", renomme.Designation);
        Assert.Equal(km.Repere, renomme.RepereOrigine);
        Assert.Contains(r.Armoire!.Appareils, a => a.Repere == "KM-Pompe" && a.Manuel);
        Assert.Contains(r.Nomenclature, e => e.Repere.Contains("KM-Pompe"));

        var chemin = Path.Combine(Path.GetTempPath(), $"renommage-{Guid.NewGuid()}{FichierProjet.Extension}");
        try
        {
            FichierProjet.Enregistrer(projet, chemin);
            Assert.Contains(CalculProjet.Calculer(FichierProjet.Ouvrir(chemin)).Arbre.Parcourir(), e => e.Repere == "KM-Pompe");
        }
        finally { File.Delete(chemin); }
    }

    [Fact]
    public void Explications_de_l_armoire_couvrent_zones_champ_et_goulottes()
    {
        var titres = ExplicationsArmoire.Rubriques.Select(r => r.Titre).ToList();
        Assert.Contains("Zones CEM", titres);
        Assert.Contains("Champ magnétique", titres);
        Assert.Contains("Fils et goulottes", titres);
    }

    [Theory]
    [InlineData("Contacteur", "3RT2026-1BB40", 25, 3, 45, 85, 107)]
    [InlineData("Contacteur", "LC1D09BD", 9, 3, 45, 77, 95)]
    [InlineData("Disjoncteur", "A9F74316", 16, 3, 54, 85, 78.5)]
    [InlineData("Disjoncteur moteur", "3RV2021-4BA10", 20, 3, 45, 97, 97)]
    [InlineData("Interrupteur-sectionneur", "VCF3", 63, 3, 60, 83, 65)]
    [InlineData("Interrupteur-sectionneur", "3LD interrupteur principal 16 A 3P", 16, 3, 67, 84, 92.5)]
    [InlineData("Interrupteur-sectionneur", "INS250 3P", 200, 3, 140, 136, 96)]
    [InlineData("Démarreur progressif", "ATS22 7.5 kW 400 V", 15, 3, 130, 265, 169)]
    [InlineData("Sectionneur porte-fusibles", "DF143", 32, 3, 79.5, 110.8, 76.5)]
    [InlineData("Parafoudre", "Acti9 iPRD40r, 4 pôles", 0, 4, 72, 85, 69)]
    [InlineData("Relais de contrôle", "RM17TG00", 0, 3, 17.5, 90, 65.5)]
    public void Cotes_relevees_remplacent_la_moyenne_de_la_famille(string famille, string reference, double courant, int poles,
        double largeur, double hauteur, double profondeur)
    {
        var e = new ElementMateriel { Famille = famille, Reference = reference }.Avec(courant, poles, plageMax: famille == "Disjoncteur moteur" ? courant : 0);
        var c = Encombrements.Estimer(e)!;
        Assert.Equal((largeur, hauteur, profondeur), (c.Largeur, c.Hauteur, c.Profondeur));
    }

    [Fact]
    public void Catalogue_des_cotes_a_des_motifs_valides_et_des_sources()
    {
        var cotes = CatalogueEncombrements.Charger();
        Assert.True(cotes.Count >= 50);
        Assert.All(cotes, c =>
        {
            _ = new System.Text.RegularExpressions.Regex(c.Motif);
            Assert.StartsWith("http", c.Source);
            Assert.True(c.Largeur > 0 && c.Hauteur > 0 && c.Profondeur > 0);
        });
    }

    [Fact]
    public void Alimentation_reglable_prefere_un_modele_a_sortie_reglable()
    {
        var catalogue = CatalogueAlimentations.Charger();
        var fixe = CatalogueAlimentations.Choisir("Siemens", 4, "230", catalogue)!;
        var reglable = CatalogueAlimentations.Choisir("Siemens", 4, "230", catalogue, reglable: true)!;

        Assert.Equal("", fixe.Reglage);
        Assert.Equal("24-28 V", reglable.Reglage);
        Assert.True(reglable.Courant >= 4);
        Assert.NotNull(CatalogueAlimentations.Choisir("Siemens", 30, "400-3P", catalogue, reglable: true));
    }

    [Fact]
    public void Pertes_relevees_sur_la_fiche_remplacent_l_estimation()
    {
        var lc1d = new ElementMateriel { Famille = "Contacteur", Reference = "LC1D09BD" }.Avec(9, 3);
        Assert.Equal(6.0, Encombrements.Estimer(lc1d)!.DissipationW);
        var xpsu = CatalogueEncombrements.Trouver("Sécurité", "XPSUDN13AP", 0)!;
        Assert.Equal((45.0, 100.0, 120.0), (xpsu.Largeur, xpsu.Hauteur, xpsu.Profondeur));
        Assert.Equal("3RT2036-1NB30", CatalogueReferences.Bobine("3RT2036-1NB30", TensionCommande.Alternatif24));
        Assert.Equal("3RT2036-1AP00", CatalogueReferences.Bobine("3RT2036-1NB30", TensionCommande.Alternatif230));
    }
}
