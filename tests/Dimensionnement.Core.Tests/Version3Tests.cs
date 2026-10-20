using System.Xml.Linq;
using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;
using Dimensionnement.Core.Persistance;
using Dimensionnement.Core.Rapport;

namespace Dimensionnement.Core.Tests;

public class Version3Tests
{
    /// <summary>Machine type : variateur 7,5 kW, deux moteurs directs, une résistance, alimentation 24 V, automate et AU.</summary>
    internal static Projet ProjetMachine()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 7.5, CourantPlaque = 15.2, Demarrage = ModeDemarrage.Variateur });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2, CourantPlaque = 4.9 });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.1, CourantPlaque = 2.6, Demarrage = ModeDemarrage.Inverseur });
        projet.Ajouter(new DepartResistif { PuissanceKw = 9 });
        projet.Ajouter(new DepartAlimentation24V());
        projet.Automate.Actif = true;
        projet.Automate.EntreesTor = 24;
        projet.Automate.SortiesTor = 16;
        projet.Automate.EntreesAnalogiques = 2;
        projet.Securite.Fonctions.Add(new FonctionSecurite { Nom = "Arrêt d'urgence", PLr = NiveauPerformance.d, NombreCapteurs = 2 });
        return projet;
    }

    static Forcage Force(double valeur) => new() { Valeur = valeur, Motif = "test", Auteur = "BE" };

    [Fact]
    public void Armoire_normalisee_avec_zone_propre_au_dessus_de_la_zone_sale()
    {
        var a = CalculProjet.Calculer(ProjetMachine()).Armoire!;

        Assert.Equal((600, 1400, 400, 1), (a.Largeur, a.Hauteur, a.Profondeur, a.Cellules));
        Assert.Equal("Coffret mural", a.Type);
        Assert.DoesNotContain(a.Note.Lignes, l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme);
        var basPropre = a.Appareils.Where(x => x.Zone == ZoneCem.Propre).Max(x => x.Y + x.Hauteur);
        var hautSale = a.Appareils.Where(x => x.Zone == ZoneCem.Sale).Min(x => x.Y);
        Assert.True(hautSale - basPropre >= CalculArmoire.SeparationCem);
        Assert.Contains(a.Appareils, x => x.Repere == "X1");
        Assert.Contains(a.Appareils, x => x.Repere == "X2");
        Assert.DoesNotContain(a.Appareils, x => x.Famille is "Moteur" or "Câble" or "Fusible");
        Assert.All(a.Appareils, x => Assert.True(x.X + x.Largeur <= a.Largeur - CalculArmoire.MargeLaterale + 0.1));
    }

    [Fact]
    public void Largeur_forcee_relance_l_implantation()
    {
        var projet = ProjetMachine();
        projet.Armoire.Forcages[CalculArmoire.CodeLargeur] = Force(800);
        projet.Armoire.Forcages[CalculArmoire.CodeHauteur] = Force(1400);

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.Equal((800, 1400), (a.Largeur, a.Hauteur));
        Assert.All(a.Appareils, x => Assert.True(x.X + x.Largeur <= 800 - CalculArmoire.MargeLaterale + 0.1));
        Assert.Contains(a.Note.Lignes, l => l.Code == CalculArmoire.CodeLargeur && l.Statut == StatutCalcul.Forcee);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Implantation" && l.Statut == StatutCalcul.Conforme);
    }

    [Fact]
    public void Armoire_trop_petite_est_non_conforme()
    {
        var projet = ProjetMachine();
        projet.Armoire.Forcages[CalculArmoire.CodeLargeur] = Force(400);
        projet.Armoire.Forcages[CalculArmoire.CodeHauteur] = Force(500);

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Implantation" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Appareil_sensible_deplace_en_zone_sale_et_chevauchement_sont_signales()
    {
        var projet = ProjetMachine();
        var auto = CalculProjet.Calculer(projet).Armoire!;
        var u1 = auto.Appareils.Single(x => x.Repere == "U1");
        var km2 = auto.Appareils.Single(x => x.Repere == "KM2");
        projet.Armoire.Positions["API1"] = new PositionManuelle { X = km2.X, Y = km2.Y };

        var a = CalculProjet.Calculer(projet).Armoire!;

        var api = a.Appareils.Single(x => x.Repere == "API1");
        Assert.True(api.Manuel);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Zone de API1" && l.Statut == StatutCalcul.Alerte);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Chevauchements" && l.Statut == StatutCalcul.Alerte);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Champ des sources de perturbations" && l.Statut == StatutCalcul.Alerte
                                            && l.Message.Contains("API1") && l.Message.Contains(u1.Repere));
    }

    [Fact]
    public void Surface_effective_selon_IEC_60890()
    {
        // Coffret 600 × 1400 × 400 adossé : 1,4 × 0,6 × (1,4 + 0,4) + 1,8 × 0,4 × 1,4 = 2,52 m².
        Assert.Equal(2.52, CalculThermique.SurfaceEffective(0.6, 1.4, 0.4, InstallationEnveloppe.AdosseeAuMur), 3);
        // Autonome : 1,8 × 1,4 × 1,0 + 1,4 × 0,24 = 2,856 m².
        Assert.Equal(2.856, CalculThermique.SurfaceEffective(0.6, 1.4, 0.4, InstallationEnveloppe.Autonome), 3);
    }

    [Fact]
    public void Ventilation_dimensionnee_sur_la_puissance_forcee()
    {
        var projet = ProjetMachine();
        projet.Armoire.Forcages[CalculThermique.CodePuissance] = Force(500);

        var t = CalculProjet.Calculer(projet).Thermique!;

        // Évacué par l'enveloppe : 5,5 × 2,52 × 5 = 69,3 W ; V = 3,1 × (500 − 69,3) / 5 = 267 m³/h ; × 1,3 = 347 → 550 m³/h.
        Assert.Equal(SolutionThermique.Ventilation, t.Solution);
        Assert.Equal(267, t.DebitNecessaire, 0);
        Assert.Equal(550, t.Ventilateur);
        Assert.Equal(75, t.Resistance);
    }

    [Fact]
    public void Climatisation_si_l_ambiance_depasse_la_temperature_admise()
    {
        var projet = ProjetMachine();
        projet.Armoire.TemperatureAmbianteMax = 45;

        var t = CalculProjet.Calculer(projet).Thermique!;

        Assert.Equal(SolutionThermique.Climatisation, t.Solution);
        // L'ambiance plus chaude ajoute k × Ae × 5 K aux pertes internes.
        Assert.Equal(t.PuissanceDissipee + 5.5 * t.SurfaceEffective * 5, t.PuissanceFrigorifique, 1);
        Assert.True(t.Climatiseur >= t.PuissanceFrigorifique);
    }

    [Fact]
    public void Pertes_reduites_par_le_taux_de_charge()
    {
        var r = CalculProjet.Calculer(ProjetMachine());
        var postes = CalculThermique.Postes(r);

        // Q2 : disjoncteur moteur 4–6,3 A réglé à 4,9 A ; pertes nominales 1,5 + 0,2 × 6,3 W, charge (4,9 / 4,9)² = 1.
        Assert.Equal(1.5 + 0.2 * 6.3, postes.Single(p => p.Repere == "Q2").PuissanceW, 2);
        // U1 : variateur 7,5 kW, 15 + 30 × 7,5 = 240 W.
        Assert.Equal(240, postes.Single(p => p.Repere == "U1").PuissanceW, 1);
    }

    [Fact]
    public void Selectivite_absente_si_le_general_est_trop_proche_du_depart()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartResistif { PuissanceKw = 9 });

        var sel = CalculProjet.Calculer(projet).Selectivite!;

        // Un seul départ de 13 A : le général est aussi de 13 A, rapport 1.
        Assert.Equal(NiveauSelectivite.Absente, sel.Lignes.Single().Niveau);
        Assert.Contains(sel.Note.Lignes, l => l.Statut == StatutCalcul.Alerte);
    }

    [Fact]
    public void Selectivite_partielle_jusqu_au_magnetique_du_general()
    {
        var sel = CalculProjet.Calculer(ProjetMachine()).Selectivite!;

        var d02 = sel.Lignes.Single(l => l.Depart == "D02");
        Assert.Equal(NiveauSelectivite.Partielle, d02.Niveau);
        Assert.Equal(160, d02.Limite);
        Assert.Equal(NiveauSelectivite.AVerifier, sel.Lignes.Single(l => l.Depart == "D01").Niveau);
    }

    [Theory]
    [InlineData(FabricantMateriel.Schneider, "GV2ME10", "LC1D09BD")]
    [InlineData(FabricantMateriel.Siemens, "3RV2011-1FA10", "3RT2016-1BB41")]
    [InlineData(FabricantMateriel.ABB, "MS132-6.3", "AF09-30-10-13")]
    public void References_du_fabricant_retenu(FabricantMateriel fabricant, string disjoncteurMoteur, string contacteur)
    {
        var projet = ProjetMachine();
        projet.References.Fabricant = fabricant;

        var r = CalculProjet.Calculer(projet);

        var elements = r.Arbre.Parcourir().ToList();
        Assert.Equal(disjoncteurMoteur, elements.Single(e => e.Repere == "Q2").Reference);
        Assert.Equal(contacteur, elements.Single(e => e.Repere == "KM2").Reference);
        Assert.StartsWith(fabricant.ToString(), elements.Single(e => e.Repere == "Q2").Fabricant);
    }

    [Fact]
    public void Disjoncteur_modulaire_reference_par_modele()
    {
        var r = CalculProjet.Calculer(ProjetMachine());

        // Q4 : 13 A courbe B, 3 pôles → Acti9 iC60N A9F73313.
        Assert.Equal("A9F73313", r.Arbre.Parcourir().Single(e => e.Repere == "Q4").Reference);
    }

    [Fact]
    public void Variateur_absent_chez_Legrand_pris_chez_un_autre_fabricant_et_reference_forcee_respectee()
    {
        var projet = ProjetMachine();
        projet.References.Fabricant = FabricantMateriel.Legrand;
        projet.References.Forcees["KM2"] = "REF-CLIENT-42";

        var r = CalculProjet.Calculer(projet);

        var elements = r.Arbre.Parcourir().ToList();
        Assert.StartsWith("Schneider", elements.Single(e => e.Repere == "U1").Fabricant);
        Assert.Equal("REF-CLIENT-42", elements.Single(e => e.Repere == "KM2").Reference);
        Assert.Contains(r.References!.Note.Lignes, l => l.Libelle == "Autre fabricant" && l.Message.Contains("U1"));
    }

    [Fact]
    public void Liste_de_devis_complete()
    {
        var r = CalculProjet.Calculer(ProjetMachine());
        var devis = ListeDevis.Generer(r);

        Assert.Contains(devis, l => l.Categorie == ListeDevis.Enveloppe && l.Designation == "Coffret électrique mural" && l.Quantite == 1);
        Assert.Contains(devis, l => l.Designation == "Ventilateur à filtre");
        Assert.Contains(devis, l => l.Reference == "GV2ME10" && l.Quantite == 1);
        Assert.Contains(devis, l => l.Reference == "LC1D09BD" && l.Quantite == 3);
        // Trois câbles moteur/résistance 4G1,5 de 10 m sans blindage, + 10 % : 33 m ; le câble du variateur est blindé.
        Assert.Contains(devis, l => l.Designation == "Câble cuivre PVC" && l.Caracteristiques == "4G1,5 mm²" && l.Quantite == 33);
        Assert.Contains(devis, l => l.Designation.StartsWith("Câble moteur blindé") && l.Quantite == 11);
        Assert.Contains(devis, l => l.Designation == "Presse-étoupe CEM laiton" && l.Quantite == 1);
        Assert.Contains(devis, l => l.Designation == "Borne de terre (vert/jaune)" && l.Quantite == 4);
        Assert.Contains(devis, l => l.Designation == "Bloc de liaison disjoncteur moteur / contacteur" && l.Quantite == 2);
        Assert.DoesNotContain(devis, l => l.Designation == "Moteur");
    }

    [Fact]
    public void Schema_unifilaire_et_vue_d_armoire_en_SVG_valide()
    {
        var r = CalculProjet.Calculer(ProjetMachine());

        var unifilaire = XDocument.Parse(SchemaUnifilaire.Svg(r));
        var textes = unifilaire.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value).ToList();
        foreach (var repere in new[] { "QS0", "QG", "Q1", "U1", "M1", "KM2", "KM3.1", "R4", "G5", "D05" })
            Assert.Contains(repere, textes);
        Assert.Contains(textes, t => t.Contains("KM3.2"));

        var armoire = XDocument.Parse(SchemaArmoire.Svg(r.Armoire!));
        Assert.Contains(armoire.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value), t => t == "API1");
    }

    [Fact]
    public void Positions_et_references_forcees_sont_enregistrees_dans_le_projet()
    {
        var projet = ProjetMachine();
        projet.Armoire.Positions["U1"] = new PositionManuelle { X = 120, Y = 800 };
        projet.Armoire.Installation = InstallationEnveloppe.Autonome;
        projet.References.Fabricant = FabricantMateriel.Siemens;
        projet.References.Forcees["Q1"] = "5SL6316-7";

        var relu = FichierProjet.Deserialiser(FichierProjet.Serialiser(projet));

        Assert.Equal(800, relu.Armoire.Positions["U1"].Y);
        Assert.Equal(InstallationEnveloppe.Autonome, relu.Armoire.Installation);
        Assert.Equal(FabricantMateriel.Siemens, relu.References.Fabricant);
        Assert.Equal("5SL6316-7", relu.References.Forcees["Q1"]);
    }
}
