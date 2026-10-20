using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class Version5Tests
{
    static Projet ProjetBarriere(TypeFonctionSecurite type, NiveauPerformance plr = NiveauPerformance.e, int nombre = 1)
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, CourantPlaque = 8.5 });
        projet.Ajouter(new DepartAlimentation24V());
        projet.Securite.Fonctions.Add(new FonctionSecurite
        {
            Nom = "Accès poste", Type = type, PLr = plr, NombreCapteurs = nombre, Departs = { 1 },
            FabricantCapteur = FabricantCapteurSecurite.Sick, Detection = DetectionBarriere.Main, HauteurProtegee = 1000, Portee = 4,
        });
        return projet;
    }

    [Fact]
    public void Barriere_type_4_choisie_hors_plaque_avec_protection_24V_dans_l_armoire()
    {
        var r = CalculProjet.Calculer(ProjetBarriere(TypeFonctionSecurite.BarriereImmaterielle, nombre: 2));

        var capteurs = r.Arbre.Parcourir().Where(e => e.Famille == "Capteur de sécurité").ToList();
        Assert.Equal(2, capteurs.Count);
        Assert.All(capteurs, c => Assert.True(c.HorsArmoire));
        Assert.StartsWith("Sick – deTec4 Core", capteurs[0].Fabricant);
        // Hauteur retenue : 300 + n × 150 ≥ 1000 → 1050 mm.
        Assert.Contains("hauteur protégée 1050 mm", capteurs[0].Calibre);
        var protection = r.Arbre.Parcourir().Single(e => e.Repere == CalculSecurite.ReperePotectionCapteurs);
        Assert.Contains(capteurs[0], protection.Enfants);
        Assert.DoesNotContain(r.Armoire!.Appareils, a => a.Famille == "Capteur de sécurité");
        Assert.Contains(r.Armoire.Appareils, a => a.Repere == CalculSecurite.ReperePotectionCapteurs);
        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith("Barrière immatérielle"));
    }

    [Fact]
    public void Barriere_PL_c_prend_un_type_2_et_PL_e_un_type_4()
    {
        var catalogue = CatalogueCapteursSecurite.Charger();
        var f = ProjetBarriere(TypeFonctionSecurite.BarriereImmaterielle, NiveauPerformance.c).Securite.Fonctions[0];
        Assert.Equal(2, CatalogueCapteursSecurite.Choisir(f, catalogue)!.Value.Modele.TypeIec);
        f.PLr = NiveauPerformance.e;
        Assert.Equal(4, CatalogueCapteursSecurite.Choisir(f, catalogue)!.Value.Modele.TypeIec);
        f.Detection = DetectionBarriere.Doigt;
        Assert.Equal(14, CatalogueCapteursSecurite.Choisir(f, catalogue)!.Value.Modele.Resolution);
    }

    [Fact]
    public void Scrutateur_signale_PL_e_impossible_et_repli_de_marque()
    {
        var projet = ProjetBarriere(TypeFonctionSecurite.ScrutateurLaser, NiveauPerformance.e);
        var r = CalculProjet.Calculer(projet);

        var scrutateur = r.Arbre.Parcourir().Single(e => e.Famille == "Capteur de sécurité");
        Assert.StartsWith("Datasensing", scrutateur.Fabricant);
        Assert.Contains(r.Securite!.Note.Lignes, l => l.Libelle.StartsWith("PL du scrutateur") && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Devis_reprend_cables_M12_des_barrieres()
    {
        var r = CalculProjet.Calculer(ProjetBarriere(TypeFonctionSecurite.BarriereImmaterielle));
        var devis = ListeDevis.Generer(r);

        Assert.Contains(devis, l => l.Designation.StartsWith("Câble de raccordement capteur") && l.Caracteristiques.Contains("8 broches"));
        Assert.Contains(devis, l => l.Designation.StartsWith("Barrière immatérielle") && l.Caracteristiques.Contains("résolution 30 mm"));
    }

    [Fact]
    public void Accessoires_CEM_fournis_avec_le_variateur_ne_sont_pas_chiffres()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 5.5, Demarrage = ModeDemarrage.Variateur, MarqueVariateur = MarqueVariateur.ABB });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, Demarrage = ModeDemarrage.Variateur, MarqueVariateur = MarqueVariateur.Siemens });
        var devis = ListeDevis.Generer(CalculProjet.Calculer(projet));

        Assert.DoesNotContain(devis, l => l.Designation.StartsWith("Filtre CEM"));
        var colliers = devis.Single(l => l.Designation.StartsWith("Collier de reprise"));
        Assert.Equal(2, colliers.Quantite);
        Assert.Equal("U2", colliers.Reperes);
        Assert.Contains(devis, l => l.Designation == "Presse-étoupe CEM laiton côté moteur" && l.Quantite == 2);
    }

    [Fact]
    public void Selectivite_masquee_quand_tout_est_bon()
    {
        var projet = new Projet();
        for (var i = 0; i < 4; i++) projet.Ajouter(new DepartResistif { PuissanceKw = 2 });
        projet.Reseau.IccKiloAmperes = 0.05;
        var r = CalculProjet.Calculer(projet);

        Assert.False(r.Selectivite!.AttentionRequise);
    }
}
