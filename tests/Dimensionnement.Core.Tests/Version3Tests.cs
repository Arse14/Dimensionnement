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

        // Plus petit coffret Rittal AX dont la plaque de montage reçoit le matériel avec 25 % de réserve, borniers en bas.
        Assert.Equal((1000, 1000, 300, 1), (a.Largeur, a.Hauteur, a.Profondeur, a.Cellules));
        Assert.Equal("Coffret mural", a.Type);
        Assert.Equal("AX 1000 × 1000 × 300", a.Reference);
        Assert.Equal((940, 940), (a.PlaqueLargeur, a.PlaqueHauteur));
        Assert.True(!a.Note.Lignes.Any(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme), string.Join(" || ", a.Note.Lignes.Where(l => l.Statut is StatutCalcul.Alerte or StatutCalcul.NonConforme).Select(l => l.Libelle + " : " + l.Message)));
        var basPropre = a.Appareils.Where(x => x.Zone == ZoneCem.Propre && x.Famille != "Bornier").Max(x => x.Y + x.Hauteur);
        var hautSale = a.Appareils.Where(x => x.Zone == ZoneCem.Sale).Min(x => x.Y);
        Assert.True(hautSale - basPropre >= CalculArmoire.SeparationCem);
        Assert.Contains(a.Appareils, x => x.Repere == "X1");
        Assert.Contains(a.Appareils, x => x.Repere == "X2");
        Assert.DoesNotContain(a.Appareils, x => x.Famille is "Moteur" or "Câble" or "Fusible");
        Assert.All(a.Appareils, x => Assert.True(x.X >= a.PlaqueX + CalculArmoire.MargeLaterale - 0.1
                                                 && x.X + x.Largeur <= a.PlaqueX + a.PlaqueLargeur - CalculArmoire.MargeLaterale + 0.1));
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
        var km2 = auto.Appareils.Single(x => x.Repere == "KM2");
        projet.Armoire.Positions["API1"] = new PositionManuelle { X = km2.X, Y = km2.Y };

        var a = CalculProjet.Calculer(projet).Armoire!;

        var api = a.Appareils.Single(x => x.Repere == "API1");
        Assert.True(api.Manuel);
        Assert.Contains(a.Note.Lignes, l => l.Libelle.StartsWith("Mélange de zones") && l.Libelle.Contains("API1") && l.Statut == StatutCalcul.Alerte);
        // La rangée qui porte l'automate reste sale : le niveau suit l'appareil le plus émissif.
        Assert.Equal(ZoneCem.Sale, a.Bandes.First(b => CalculArmoire.Dans(b, new[] { api }).Any()).Zone);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Chevauchements" && l.Statut == StatutCalcul.Alerte);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Champ des sources de perturbations" && l.Statut == StatutCalcul.Alerte
                                            && l.Message.Contains("API1 reçoit"));
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

        // Coffret 1000 × 1000 × 300 adossé : Ae = 1,4 × 1,0 × 1,3 + 1,8 × 0,3 × 1,0 = 2,36 m².
        // Évacué par l'enveloppe : 5,5 × 2,36 × 5 = 65 W ; V = 3,1 × (500 − 65) / 5 = 270 m³/h ; × 1,3 = 351 → 550 m³/h.
        Assert.Equal(SolutionThermique.Ventilation, t.Solution);
        Assert.Equal(269.8, t.DebitNecessaire, 1);
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
        // U1 : ATV320 7,5 kW du catalogue des variateurs, pertes 25 + 28 × 7,5 = 235 W.
        Assert.Equal(235, postes.Single(p => p.Repere == "U1").PuissanceW, 1);
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
        var projet = ProjetMachine();
        projet.References.Fabricant = FabricantMateriel.Schneider;
        var r = CalculProjet.Calculer(projet);

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
        // Legrand ne fait pas de variateurs : le variateur est choisi chez Schneider.
        Assert.Contains(r.Departs[0].Note.Lignes, l => l.Libelle == "Variateur retenu" && l.Message.Contains("ATV320"));
    }

    [Fact]
    public void Liste_de_devis_complete()
    {
        var projet = ProjetMachine();
        projet.References.Fabricant = FabricantMateriel.Schneider;
        var r = CalculProjet.Calculer(projet);
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
    public void Vue_d_armoire_en_SVG_valide()
    {
        var r = CalculProjet.Calculer(ProjetMachine());

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

public class ChampMagnetiqueTests
{
    [Fact]
    public void Champ_d_un_conducteur_et_rayon_au_seuil()
    {
        // 10 A × k 3 = 30 A équivalents, e = 43,3 mm : H = 30 × 0,0433 / (2π × 0,1²) ≈ 20,7 A/m à 100 mm ; 30 A/m à 83 mm.
        var s = new SourceChamp("U1", "Variateur", 0, 0, 10, 3, 43.3, 25);

        Assert.Equal(20.7, s.H(100, 0), 1);
        Assert.Equal(83.0, s.Rayon(30), 1);
        Assert.Equal(30, s.H(s.Rayon(30), 0), 6);
        Assert.Equal(s.H(25, 0), s.H(5, 0));
    }

    [Fact]
    public void Variateur_choisi_par_marque_avec_son_encombrement()
    {
        var projet = Version3Tests.ProjetMachine();
        var d01 = (DepartMoteur)projet.Departs[0];
        d01.MarqueVariateur = MarqueVariateur.ABB;

        var r = CalculProjet.Calculer(projet);

        var u1 = r.Arbre.Parcourir().Single(e => e.Repere == "U1");
        Assert.StartsWith("ABB", u1.Fabricant);
        Assert.StartsWith("ACS380", u1.Reference);
        Assert.True(u1.LargeurMm > 0 && u1.HauteurMm > 0);
        Assert.Equal(u1.LargeurMm, r.Armoire!.Appareils.Single(a => a.Repere == "U1").Largeur);
    }

    [Fact]
    public void Variateurs_regroupes_cote_a_cote_dans_la_zone_sale()
    {
        var projet = new Projet();
        for (var i = 0; i < 4; i++)
            projet.Ajouter(new DepartMoteur { PuissanceKw = 4, Demarrage = ModeDemarrage.Variateur });
        for (var i = 0; i < 3; i++)
            projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2 });

        var a = CalculProjet.Calculer(projet).Armoire!;

        var variateurs = a.Appareils.Where(x => x.Famille == "Variateur").ToList();
        var contacteurs = a.Appareils.Where(x => x.Famille == "Contacteur").ToList();
        // Côte à côte sur une même ligne, sans autre appareil intercalé : soit sur le côté, soit en rangée au bas de la zone sale.
        Assert.Single(variateurs.Select(v => v.Y).Distinct());
        Assert.DoesNotContain(a.Appareils.Except(variateurs), x =>
            x.X < variateurs.Max(v => v.X + v.Largeur) && x.X + x.Largeur > variateurs.Min(v => v.X)
            && x.Y < variateurs.Max(v => v.Y + v.Hauteur) && x.Y + x.Hauteur > variateurs.Min(v => v.Y));
        Assert.True(variateurs.Min(v => v.Y) >= contacteurs.Min(c => c.Y) || variateurs.Min(v => v.X) > contacteurs.Max(c => c.X + c.Largeur));
    }

    [Fact]
    public void Variateurs_sur_le_cote_droit_quand_la_place_le_permet()
    {
        var projet = new Projet();
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.5, Demarrage = ModeDemarrage.Variateur });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 1.5, Demarrage = ModeDemarrage.Variateur });
        for (var i = 0; i < 6; i++)
            projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2, Demarrage = ModeDemarrage.Inverseur });

        var a = CalculProjet.Calculer(projet).Armoire!;

        var variateurs = a.Appareils.Where(x => x.Famille == "Variateur").ToList();
        var contacteurs = a.Appareils.Where(x => x.Famille == "Contacteur").ToList();
        Assert.True(variateurs.Min(v => v.X) > contacteurs.Max(c => c.X + c.Largeur));
        Assert.Contains(a.Bandes, b => !b.Rail && b.Zone == ZoneCem.Sale);
    }

    [Fact]
    public void Taille_imposee_trop_petite_signalee_impossible()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.TailleImposee = true;
        (projet.Armoire.LargeurImposee, projet.Armoire.HauteurImposee, projet.Armoire.ProfondeurImposee) = (400, 500, 250);

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.False(a.Tient);
        Assert.Contains("Impossible", a.Alerte);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Implantation" && l.Statut == StatutCalcul.NonConforme);
    }

    static Projet QuatreVariateurs(double kw)
    {
        var projet = new Projet();
        for (var i = 0; i < 4; i++) projet.Ajouter(new DepartMoteur { PuissanceKw = kw, Demarrage = ModeDemarrage.Variateur });
        projet.Armoire.Marque = MarqueEnveloppe.Schneider;
        projet.Armoire.TailleImposee = true;
        (projet.Armoire.LargeurImposee, projet.Armoire.HauteurImposee, projet.Armoire.ProfondeurImposee) = (600, 800, 300);
        return projet;
    }

    [Fact]
    public void Taille_imposee_trop_juste_allege_les_goulottes_en_gardant_la_place_libre()
    {
        var a = CalculProjet.Calculer(QuatreVariateurs(1.5)).Armoire!;

        Assert.True(a.Tient);
        Assert.Contains("goulottes", a.Alerte);
        // Goulottes allégées d'abord ; la rangée des borniers prend ensuite un peu de la place libre.
        Assert.Equal(15, a.ReserveEffective, 3);
        Assert.DoesNotContain(a.Note.Lignes, l => l.Libelle == "Implantation" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Ce_qui_n_entre_pas_attend_a_droite_et_se_place_a_la_main()
    {
        var projet = QuatreVariateurs(4);
        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.False(a.Tient);
        Assert.True(a.LargeurAttente > 0);
        var dehors = a.Appareils.Where(x => x.X >= a.LargeurTotale).ToList();
        Assert.NotEmpty(dehors);
        Assert.All(dehors, x => Assert.True(x.Y >= 0 && x.Y + x.Hauteur <= a.Hauteur));

        // L'utilisateur les pose lui-même sur la plaque, sans chevauchement (trois en bas, le quatrième au-dessus à droite) :
        // l'implantation manuelle est retenue.
        var variateurs = a.Appareils.Where(v => v.Famille == "Variateur").ToList();
        var bas = a.PlaqueY + a.PlaqueHauteur - variateurs[0].Hauteur;
        for (var i = 0; i < 3; i++)
            projet.Armoire.Positions[variateurs[i].Cle] = new PositionManuelle { X = a.PlaqueX + i * variateurs[i].Largeur, Y = bas };
        projet.Armoire.Positions[variateurs[3].Cle] = new PositionManuelle
            { X = a.PlaqueX + a.PlaqueLargeur - variateurs[3].Largeur, Y = bas - variateurs[3].Hauteur };
        // Le bornier, resté lui aussi à placer, va en haut à droite, à côté des disjoncteurs.
        var bornier = a.Appareils.Single(x => x.Famille == "Bornier");
        projet.Armoire.Positions[bornier.Cle] = new PositionManuelle { X = a.PlaqueX + a.PlaqueLargeur - bornier.Largeur - 20, Y = a.PlaqueY + 60 };
        var manuel = CalculProjet.Calculer(projet).Armoire!;

        Assert.True(manuel.Tient, manuel.Alerte);
        Assert.Contains("à la main", manuel.Alerte);
        Assert.Equal(0, manuel.LargeurAttente);
    }

    [Fact]
    public void Materiel_deporte_dans_un_coffret_dimensionne_pour_lui()
    {
        var projet = QuatreVariateurs(4);
        var avant = CalculProjet.Calculer(projet);
        Assert.False(avant.Armoire!.Tient);

        var coffret = new CoffretDeporte { Nom = "Coffret variateurs", Marque = MarqueEnveloppe.Schneider };
        projet.Armoire.Coffrets.Add(coffret);
        foreach (var v in avant.Armoire.Appareils.Where(v => v.Famille == "Variateur").Take(2))
            projet.Armoire.Affectations[CalculArmoire.CleAffectation(v)] = coffret.Id;
        var r = CalculProjet.Calculer(projet);
        var principale = r.Armoire!;
        var deporte = Assert.Single(principale.Coffrets);

        Assert.True(principale.Tient, principale.Alerte);
        Assert.Equal(2, principale.Appareils.Count(a => a.Famille == "Variateur"));
        Assert.Equal(2, deporte.Appareils.Count(a => a.Famille == "Variateur"));
        Assert.Equal("Coffret variateurs", deporte.Nom);
        Assert.True(deporte.Tient);
        Assert.False(deporte.TailleImposee);
        // Chaque enveloppe chauffe avec ses propres appareils.
        Assert.True(deporte.Thermique!.PuissanceDissipee > 0);
        Assert.True(principale.Thermique!.PuissanceDissipee < avant.Thermique!.PuissanceDissipee);
        // Deux enveloppes au devis.
        var devis = ListeDevis.Generer(r);
        Assert.Equal(2, devis.Count(l => l.Categorie == ListeDevis.Enveloppe && l.Designation.StartsWith("Coffret électrique")));
    }

    [Fact]
    public void Taille_imposee_juste_reduit_la_reserve()
    {
        var projet = Version3Tests.ProjetMachine();
        var auto = CalculProjet.Calculer(projet).Armoire!;
        projet.Armoire.ReservePourcent = 60;
        projet.Armoire.TailleImposee = true;
        (projet.Armoire.LargeurImposee, projet.Armoire.HauteurImposee, projet.Armoire.ProfondeurImposee) = (auto.Largeur, auto.Hauteur, auto.Profondeur);

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.True(a.Tient);
        // La place libre est gardée en priorité : les goulottes ou le dégagement sont allégés d'abord.
        Assert.True(a.ReserveEffective < 60 || a.Alerte.Contains("goulottes") || a.Alerte.Contains("dégagement"));
        Assert.NotEmpty(a.Alerte);
        Assert.Equal((auto.Largeur, auto.Hauteur), (a.Largeur, a.Hauteur));
    }

    [Fact]
    public void Enveloppe_de_la_marque_choisie()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.Marque = MarqueEnveloppe.Schneider;

        var a = CalculProjet.Calculer(projet).Armoire!;

        Assert.StartsWith("NSYS3D", a.Reference);
        Assert.StartsWith("NSYMM", a.ReferencePlaque);
    }

    [Fact]
    public void Isoligne_d_une_source_seule_est_un_cercle()
    {
        var s = new SourceChamp("U1", "Variateur", 300, 300, 10, 3, 43.3, 25);
        var grille = ChampMagnetique.Grille(new[] { s }, 600, 600, 5);

        var segments = ChampMagnetique.Isoligne(grille, 5, 30);

        Assert.NotEmpty(segments);
        Assert.All(segments, g => Assert.InRange(Math.Sqrt((g.X1 - 300) * (g.X1 - 300) + (g.Y1 - 300) * (g.Y1 - 300)), 79, 87));
    }

    [Fact]
    public void Variateur_rayonne_plus_qu_un_contacteur_de_meme_courant()
    {
        var a = CalculProjet.Calculer(Version3Tests.ProjetMachine()).Armoire!;

        var u1 = a.Sources.Single(s => s.Repere == "U1");
        Assert.Equal(3, u1.Facteur);
        Assert.Contains(a.Sources, s => s.Famille == "Contacteur" && s.Facteur == 1);
        Assert.Contains("API1", a.Expositions.Keys);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Source U1" && l.Message.Contains("30 A/m jusqu'à"));
    }

    [Fact]
    public void Cartes_d_ES_implantees_a_leurs_cotes_reelles_sans_les_accessoires()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Automate.CartesCompactes = true;

        var a = CalculProjet.Calculer(projet).Armoire!;

        var cartes = a.Appareils.Where(x => x.Repere.StartsWith("API1.")).ToList();
        Assert.NotEmpty(cartes);
        Assert.All(cartes, x => Assert.Equal(15, x.Largeur));
        Assert.Equal(100, a.Appareils.Single(x => x.Repere == "API1").Largeur);
        Assert.DoesNotContain(a.Appareils, x => x.Repere.EndsWith("-X"));
    }

    [Fact]
    public void Zone_recalculee_quand_les_sources_quittent_une_rangee()
    {
        var projet = Version3Tests.ProjetMachine();
        var auto = CalculProjet.Calculer(projet).Armoire!;
        var bande = auto.Bandes.First(b => b.Rail && b.Zone == ZoneCem.Sale);
        var sources = CalculArmoire.Dans(bande, auto.Appareils).ToList();
        // Toutes les sources partent en haut à gauche, hors de la rangée.
        var x = 0.0;
        foreach (var s in sources.Where(s => s.Zone == ZoneCem.Sale))
        {
            projet.Armoire.Positions[s.Repere] = new PositionManuelle { X = x, Y = 0 };
            x += s.Largeur;
        }
        // Un disjoncteur (intermédiaire) prend leur place.
        var q = auto.Appareils.First(d => d.Zone == ZoneCem.Intermediaire && d.Famille != "Bornier");
        projet.Armoire.Positions[q.Repere] = new PositionManuelle { X = bande.X + 60, Y = bande.Y + bande.Hauteur / 2 - q.Hauteur / 2 };

        var a = CalculProjet.Calculer(projet).Armoire!;

        var meme = a.Bandes.Single(b => Math.Abs(b.Y - bande.Y) < 0.1 && Math.Abs(b.X - bande.X) < 0.1);
        Assert.Equal(ZoneCem.Intermediaire, meme.Zone);
    }

    [Fact]
    public void Rail_ajoute_prend_la_couleur_de_ses_appareils_et_compte_au_devis()
    {
        var projet = Version3Tests.ProjetMachine();
        var auto = CalculProjet.Calculer(projet).Armoire!;
        var km2 = auto.Appareils.Single(x => x.Repere == "KM2");
        projet.Armoire.Rails.Add(new RailManuel { X = 100, Y = 200, Longueur = 300 });
        projet.Armoire.Positions["KM2"] = new PositionManuelle { X = 150, Y = 200 - km2.Hauteur / 2 };
        projet.Armoire.Goulottes.Add(new GoulotteManuelle { X = 100, Y = 300, Longueur = 300, Largeur = 60, Hauteur = 60 });

        var r = CalculProjet.Calculer(projet);
        var a = r.Armoire!;

        var rail = a.Bandes.Single(b => b.Manuel);
        Assert.Equal(ZoneCem.Sale, rail.Zone);
        Assert.Equal(auto.LongueurRail + 0.3, a.LongueurRail, 3);
        Assert.Contains(a.Cablage.Goulottes, g => g.Manuelle && g.Largeur == 300 && g.Hauteur == 60);
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Rails ajoutés à la main");
        Assert.DoesNotContain(a.Note.Lignes, l => l.Libelle == "Rail de KM2");
    }

    [Fact]
    public void Fils_estimes_par_appareil_et_goulottes_dimensionnees()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());
        var a = r.Armoire!;

        // Chaque départ amène ses conducteurs et son PE ; la commande est en 0,75 mm².
        Assert.Contains(a.Cablage.Fils, f => f.Usage == "Terre" && f.Nombre >= r.Departs.Count(d => d.Depart is not DepartAlimentation24V));
        Assert.Contains(a.Cablage.Fils, f => f.Section == CalculCablage.SectionCommande && f.Nombre > 0);
        Assert.Contains(a.Cablage.Goulottes, g => !g.Verticale);
        Assert.True(a.Cablage.Goulottes.Count(g => g.Verticale && g.Hauteur > a.PlaqueHauteur / 2) >= 2 * a.Cellules);
        Assert.All(a.Cablage.Goulottes.Where(g => !g.Manuelle), g => Assert.True(g.Remplissage <= CalculCablage.RemplissageMax + 1e-6));
        Assert.Contains(a.Note.Lignes, l => l.Libelle == "Estimation du câblage");
        var devis = ListeDevis.Generer(r);
        Assert.Contains(devis, l => l.Designation.StartsWith("Goulotte de câblage") && l.Caracteristiques.StartsWith(a.Cablage.Horizontale.Libelle));
        Assert.Contains(devis, l => l.Designation.StartsWith("Fil de câblage vert/jaune"));
    }

    [Fact]
    public void Goulotte_choisie_au_remplissage_de_50_pourcent()
    {
        // 60 fils de 2,5 mm² : 60 × π × 3,6² / 4 ≈ 611 mm² → 25 × 60 (1 500 mm², 41 %).
        var surface = CalculCablage.Encombrement(new[] { new FilsSection(2.5, 60, "Puissance") });
        Assert.Equal(new ModeleGoulotte(25, 60), CalculCablage.Choisir(surface));
        Assert.Equal(new ModeleGoulotte(25, 40), CalculCablage.Choisir(100));
    }
}
