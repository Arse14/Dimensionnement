using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class Version4Tests
{
    [Fact]
    public void Bobines_24V_AC_vont_sur_un_transformateur_24V_AC()
    {
        var projet = new Projet { TensionBobines = TensionCommande.Alternatif24 };
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, CourantPlaque = 8.5 });
        projet.Ajouter(new DepartMoteur { PuissanceKw = 2.2, CourantPlaque = 4.9, Demarrage = ModeDemarrage.Inverseur });
        var transfo = projet.Ajouter(new DepartAlimentation24V
            { Source = SourceAlimentation.Transformateur, Sortie = TensionCommande.Alternatif24, Alimentation = TypeAlimentation.Bi400 });

        var r = CalculProjet.Calculer(projet);

        // 3 bobines × 7 VA / 24 V
        Assert.Equal(3 * 7 / 24.0, r.Consommation(TensionCommande.Alternatif24), 3);
        Assert.Equal(0, r.Consommation24V, 3);
        Assert.Empty(r.SourcesManquantes);
        var element = r.Departs.Single(d => d.Depart == transfo).Materiel.Parcourir().Single(e => e.Famille == "Alimentation");
        Assert.Equal($"T{transfo.Numero}", element.Repere);
        Assert.Contains("24 V AC", element.Calibre);
        Assert.Contains(r.Arbre.Parcourir(), e => e.Famille == "Contacteur" && e.Designation.Contains("bobine 24 V AC"));
    }

    [Fact]
    public void Reference_des_contacteurs_suit_la_tension_des_bobines()
    {
        Assert.Equal("LC1D18B7", CatalogueReferences.Bobine("LC1D18BD", TensionCommande.Alternatif24));
        Assert.Equal("LC1D40AP7", CatalogueReferences.Bobine("LC1D40ABD", TensionCommande.Alternatif230));
        Assert.Equal("3RT2015-1AB01", CatalogueReferences.Bobine("3RT2015-1BB41", TensionCommande.Alternatif24));
        Assert.Equal("AF09-30-10-14", CatalogueReferences.Bobine("AF09-30-10-13", TensionCommande.Alternatif230));
        Assert.Equal("LC1D18BD", CatalogueReferences.Bobine("LC1D18BD", TensionCommande.Continu24));
    }

    [Fact]
    public void Transformateur_24V_DC_ajoute_un_redresseur_et_tient_compte_du_facteur()
    {
        var projet = new Projet();
        projet.Consommateurs24V.Add(new Consommateur24V { Designation = "Capteurs", CourantMa = 2000 });
        var t = projet.Ajouter(new DepartAlimentation24V { Source = SourceAlimentation.Transformateur, Alimentation = TypeAlimentation.Bi400 });

        var r = CalculProjet.Calculer(projet);

        var d = r.Departs.Single(x => x.Depart == t);
        // 24 V × 2 A × 1,8 × 1,25 = 108 VA → 160 VA
        var va = d.Note.Lignes.Single(l => l.Code == CodesCalcul.CalibreAlimentation24V);
        Assert.Equal(108, va.ValeurCalculee!.Value, 1);
        Assert.Equal(160, va.ValeurRetenue);
        Assert.Contains(d.Materiel.Parcourir(), e => e.Famille == "Redresseur");
    }

    [Fact]
    public void Automate_sur_24V_AC_donne_une_alerte()
    {
        var projet = new Projet();
        projet.Automate.Actif = true;
        var t = projet.Ajouter(new DepartAlimentation24V
            { Source = SourceAlimentation.Transformateur, Sortie = TensionCommande.Alternatif24, Alimentation = TypeAlimentation.Bi400 });

        var r = CalculProjet.Calculer(projet);

        // L'automate est en 24 V DC : il ne va pas sur le transformateur 24 V AC, la source 24 V DC manque.
        Assert.Contains(r.SourcesManquantes, b => b.Tension == TensionCommande.Continu24);
        Assert.DoesNotContain(r.Departs.Single(x => x.Depart == t).Note.Lignes, l => l.Libelle.StartsWith("Raccordés : Automate"));
    }

    [Fact]
    public void Transformateur_400_230_alimente_ses_departs_et_les_retire_du_bilan_principal()
    {
        var projet = new Projet();
        projet.Reseau.Alimentation = TypeAlimentation.Tri400;
        var prise = projet.Ajouter(new DepartPrise());
        var charge = projet.Ajouter(new DepartGenerique { PuissanceKw = 1, CosPhi = 1, Alimentation = TypeAlimentation.Mono230 });

        var sansTransfo = CalculProjet.Calculer(projet);
        Assert.Contains(sansTransfo.SourcesManquantes, b => b.Tension == TensionCommande.Alternatif230 && b.Departs.Count == 2);

        var ajoutees = SourcesCommande.Completer(projet, sansTransfo.Consommateurs24V, _ => SourceAlimentation.Decoupage);
        var t = Assert.Single(ajoutees);
        Assert.Equal(TensionCommande.Alternatif230, t.Sortie);
        Assert.Equal(TypeAlimentation.Bi400, t.Alimentation);
        Assert.Equal(new[] { prise.Numero, charge.Numero }, t.DepartsAlimentes);

        var r = CalculProjet.Calculer(projet);
        Assert.Empty(r.SourcesManquantes);
        var transfo = r.Arbre.Parcourir().Single(e => e.Repere == $"T{t.Numero}");
        Assert.Contains(transfo.Parcourir(), e => e.DepartId == charge.Id);
        // Charge 1 kW + prise 16 A × 230 V × 0,5 = 1,84 kVA, cos φ prise 1 → S ≈ 2,84 kVA ; + 25 % → 4000 VA
        var va = r.Departs.Single(d => d.Depart == t).Note.Lignes.Single(l => l.Code == CodesCalcul.CalibreAlimentation24V);
        Assert.Equal(4000, va.ValeurRetenue);
        Assert.Contains(r.NoteArrivee.Lignes, l => l.Libelle == "Départs derrière un transformateur");
    }

    [Fact]
    public void Ajout_automatique_d_une_alimentation_24V_DC_selon_la_preference()
    {
        var projet = new Projet { PreferenceSource = PreferenceSource.Decoupage };
        projet.Automate.Actif = true;
        var r = CalculProjet.Calculer(projet);
        Assert.Contains(r.SourcesManquantes, b => b.Tension == TensionCommande.Continu24);

        var ajoutees = SourcesCommande.Completer(projet, r.Consommateurs24V, _ => SourceAlimentation.Decoupage);

        var a = Assert.Single(ajoutees);
        Assert.False(a.EstTransformateur);
        Assert.True(a.AjoutAutomatique);
        Assert.Equal(TypeAlimentation.Mono230, a.Alimentation);
        Assert.Empty(CalculProjet.Calculer(projet).SourcesManquantes);
    }

    [Fact]
    public void Source_refusee_n_est_pas_ajoutee()
    {
        var projet = new Projet();
        projet.Automate.Actif = true;
        var r = CalculProjet.Calculer(projet);

        Assert.Empty(SourcesCommande.Completer(projet, r.Consommateurs24V, _ => null));
        Assert.Empty(SourcesCommande.Completer(projet, r.Consommateurs24V, _ => SourceAlimentation.Decoupage,
            new HashSet<string> { SourcesCommande.Cle(TensionCommande.Continu24, UsageAlimentation24V.Commande) }));
    }

    [Fact]
    public void Ihm_choisie_au_catalogue_et_comptee_au_bilan_24V()
    {
        var projet = new Projet();
        projet.Automate.Actif = true;
        projet.Automate.Ihm = 0;
        projet.Automate.Pupitres.Add(new PupitreIhm { Nom = "Pupitre", Diagonale = 7 });
        projet.Automate.Pupitres.Add(new PupitreIhm { Nom = "Confort", Diagonale = 12, Gamme = GammeIhm.Confort });

        var r = CalculProjet.Calculer(projet);

        Assert.Contains(r.Arbre.Parcourir(), e => e.Famille == "IHM" && e.Reference == "6AV2123-2GB03-0AX0");
        Assert.Contains(r.Arbre.Parcourir(), e => e.Famille == "IHM" && e.Reference == "6AV2128-3MB06-0AX1");
        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith("IHM KTP700") && Math.Abs(c.Courant - 0.23) < 1e-9);
        Assert.Equal(4, CalculAutomate.Connexions(projet.Automate));
    }

    [Fact]
    public void Ihm_schneider_selon_la_marque_de_l_automate()
    {
        var p = new PupitreIhm { Diagonale = 10 };
        Assert.Equal("HMIST6500", CatalogueIhm.Choisir(p, "Schneider", CatalogueIhm.Charger())!.Reference);
    }

    [Fact]
    public void Comfort_Panel_et_SIPLUS_au_catalogue_mais_jamais_choisis_d_office()
    {
        var catalogue = CatalogueIhm.Charger();
        var tp700 = Assert.Single(catalogue, m => m.Reference == "6AV2124-0GC01-0AX0");
        Assert.Equal((198.0, 142.0), (tp700.DecoupeLargeur, tp700.DecoupeHauteur));
        Assert.True(tp700.Verifie);
        Assert.Contains(catalogue, m => m.Reference == "6AG1124-0GC01-4AX0" && m.Gamme.StartsWith("SIPLUS"));
        Assert.Equal("6AV2128-3GB06-0AX1", CatalogueIhm.Choisir(new PupitreIhm { Diagonale = 7, Gamme = GammeIhm.Confort }, "Siemens", catalogue)!.Reference);
        Assert.Equal("6AV2124-0GC01-0AX0",
            CatalogueIhm.Choisir(new PupitreIhm { ReferenceImposee = "6av2124-0gc01-0ax0" }, "Siemens", catalogue)!.Reference);
    }

    [Fact]
    public void Catalogue_IHM_utilisateur_complete_des_nouveaux_pupitres()
    {
        var fichier = Path.GetTempFileName();
        File.WriteAllText(fichier, """[{"fabricant": "Siemens", "rang": 1, "reference": "6AV2123-2GB03-0AX0", "designation": "Mon KTP700", "diagonale": 7}]""");
        var catalogue = CatalogueIhm.Charger(fichier);
        File.Delete(fichier);
        Assert.Equal("Mon KTP700", catalogue.Single(m => m.Reference == "6AV2123-2GB03-0AX0").Designation);
        Assert.Contains(catalogue, m => m.Reference == "6AV2124-0JC01-0AX0");
    }

    [Fact]
    public void Variateur_impose_par_sa_reference_et_controle()
    {
        var projet = new Projet();
        var m = new DepartMoteur { PuissanceKw = 2.2, CourantPlaque = 5, Demarrage = ModeDemarrage.Variateur, ReferenceVariateur = "atv320u30n4c" };
        projet.Ajouter(m);
        var r = CalculProjet.Calculer(projet);
        var d = r.Departs.Single();
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Variateur imposé" && l.Statut == StatutCalcul.Conforme);
        Assert.Contains("ATV320U30N4C", string.Join(" ", d.Note.Lignes.Select(l => l.Message)));

        m.ReferenceVariateur = "ATV320U04N4C";
        d = CalculProjet.Calculer(projet).Departs.Single();
        Assert.Contains(d.Note.Lignes, l => l.Libelle == "Variateur imposé" && l.Statut == StatutCalcul.NonConforme);
    }

    [Fact]
    public void Marques_par_famille_pour_les_contacteurs_et_disjoncteurs()
    {
        var projet = new Projet();
        projet.References.Fabricant = FabricantMateriel.Schneider;
        projet.References.MarqueContacteurs = MarqueFamille.Siemens;
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, CourantPlaque = 8.5 });

        var r = CalculProjet.Calculer(projet);

        var km = r.Arbre.Parcourir().First(e => e.Famille == "Contacteur");
        Assert.StartsWith("Siemens", km.Fabricant);
        Assert.StartsWith("3RT", km.Reference);
        Assert.StartsWith("Schneider", r.Arbre.Parcourir().First(e => e.Famille == "Disjoncteur moteur").Fabricant);
    }

    [Fact]
    public void Devis_donne_les_caracteristiques_sans_reference()
    {
        var projet = new Projet { TensionBobines = TensionCommande.Alternatif230 };
        projet.Ajouter(new DepartMoteur { PuissanceKw = 4, CourantPlaque = 8.5 });

        var r = CalculProjet.Calculer(projet);
        var km = ListeDevis.Generer(r).First(l => l.Designation.StartsWith("Contacteur"));

        Assert.Contains("3P", km.Caracteristiques);
        Assert.Contains("moteur 4 kW 400 V", km.Caracteristiques);
        Assert.Contains("bobine 230 V AC", km.Designation);
    }

    [Fact]
    public void Bilan_de_puissance_explique_pas_a_pas()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());

        var etapes = ExplicationBilan.Etapes(r);

        Assert.Equal(8, etapes.Count);
        Assert.Equal("Coefficient de simultanéité ks", etapes[2].Titre);
        Assert.All(etapes, e => Assert.False(string.IsNullOrWhiteSpace(e.Pourquoi)));
        Assert.Equal(r.Departs.Count, ExplicationBilan.Lignes(r).Count);
        Assert.Equal(r.PuissanceActive / r.CoefficientSimultaneite, ExplicationBilan.Lignes(r).Sum(l => l.PUtilisee), 3);
    }
}

public class NavigateurCatalogueTests
{
    [Fact]
    public void Chaque_catalogue_se_relit_apres_reecriture_par_le_navigateur()
    {
        foreach (var f in BibliothequeCatalogues.Familles)
        {
            var articles = BibliothequeCatalogues.Lire(f.TexteEmbarque());
            Assert.NotEmpty(articles);
            articles[0][BibliothequeCatalogues.Photos] = new System.Text.Json.Nodes.JsonArray("C:/photos/a.jpg");
            BibliothequeCatalogues.Tracer(articles[0], "ArseniK");
            var texte = BibliothequeCatalogues.Ecrire(f, articles);
            Assert.Equal(articles.Count, BibliothequeCatalogues.Lire(texte).Count);
            f.LireType(texte);
        }
    }

    [Fact]
    public void Saisie_manuelle_d_un_champ_et_controle()
    {
        var f = BibliothequeCatalogues.Familles.Single(x => x.Id == "ihm");
        var catalogue = BibliothequeCatalogues.Lire(f.TexteEmbarque());
        var a = new System.Text.Json.Nodes.JsonObject { ["fabricant"] = "Siemens" };

        Assert.Null(BibliothequeCatalogues.Saisir(a, f.Champ("diagonale")!, "7,5"));
        Assert.NotNull(BibliothequeCatalogues.Saisir(a, f.Champ("largeur")!, "abc"));
        Assert.Equal("7,5 \"", BibliothequeCatalogues.Texte(a, f.Champ("diagonale")!));

        var points = BibliothequeCatalogues.Controler(f, a, catalogue);
        Assert.Contains("Référence à compléter", points);
        Assert.Contains(points, p => p.StartsWith("Largeur"));
    }

    [Fact]
    public void Filtre_par_marque_et_valeur_minimale()
    {
        var f = BibliothequeCatalogues.Familles.Single(x => x.Id == "alimentations");
        var articles = BibliothequeCatalogues.Lire(f.TexteEmbarque());

        var r = BibliothequeCatalogues.Filtrer(f, articles, "", "Siemens", null, 20, false).ToList();

        Assert.NotEmpty(r);
        Assert.All(r, a => Assert.True(BibliothequeCatalogues.Nombre(a, "courant") >= 20));
        Assert.All(r, a => Assert.Equal("Siemens", BibliothequeCatalogues.Marque(a)));
    }

    [Fact]
    public void Enregistrement_refuse_un_catalogue_illisible_par_les_calculs()
    {
        var f = BibliothequeCatalogues.Familles.Single(x => x.Id == "cartes");
        var articles = BibliothequeCatalogues.Lire(f.TexteEmbarque());
        articles[0]["voies"] = 16.5;
        var fichier = Path.Combine(Path.GetTempPath(), $"cartes-{Guid.NewGuid():N}.json");

        Assert.Throws<InvalidDataException>(() => BibliothequeCatalogues.Enregistrer(f, fichier, articles));
        Assert.False(File.Exists(fichier));
    }
}
