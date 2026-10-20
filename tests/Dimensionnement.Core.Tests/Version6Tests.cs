using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
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
}
