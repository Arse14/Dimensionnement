using Dimensionnement.Core.Armoire;
using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Modele;
using Xunit;

namespace Dimensionnement.Core.Tests;

public class EcritureMathTests
{
    static IEnumerable<NoeudMath> Tous(IEnumerable<NoeudMath> noeuds) => noeuds.SelectMany(n => n switch
    {
        FractionMath f => new[] { n }.Concat(Tous(f.Numerateur)).Concat(Tous(f.Denominateur)),
        RacineMath r => new[] { n }.Concat(Tous(r.Contenu)),
        _ => new[] { n },
    });

    [Fact]
    public void Division_en_fraction_et_racine_avec_barre()
    {
        var noeuds = EcritureMath.Analyser("I = P / (√3 × U × cos φ × η)");
        var f = Assert.Single(noeuds.OfType<FractionMath>());
        Assert.Equal(new MorceauMath("P", true, true), Assert.Single(f.Numerateur));
        Assert.Single(f.Denominateur.OfType<RacineMath>());
        // Le « × » entre lettres devient un point ; les parenthèses autour du dénominateur tombent.
        Assert.Contains(Tous(f.Denominateur).OfType<MorceauMath>(), m => m.Texte.Contains('·'));
        Assert.DoesNotContain(Tous(f.Denominateur).OfType<MorceauMath>(), m => m.Texte.Contains('('));
    }

    [Fact]
    public void Indices_et_italique()
    {
        var m = Assert.Single(EcritureMath.Analyser("Ib").OfType<MorceauMath>());
        Assert.Equal(new MorceauMath("I", true, true, "b"), m);
        Assert.Contains(EcritureMath.Analyser("I_{n,plaque} = 2,4 A").OfType<MorceauMath>(), x => x.Indice == "n,plaque");
        Assert.Equal("In plaque = 2,4 A", EcritureMath.Texte("I_{n,plaque} = 2,4 A"));
    }

    [Theory]
    [InlineData("Alimentation 230 V AC / 24 V DC")]
    [InlineData("Sélectivité Q1 / Q2")]
    [InlineData("105 × 142 × 158 mm (L × H × P)")]
    [InlineData("Ta disposition ne tient pas encore.")]
    [InlineData("1 circuit(s)")]
    public void La_prose_reste_telle_quelle(string texte)
    {
        var noeuds = EcritureMath.Analyser(texte);
        Assert.All(noeuds, n => Assert.Equal(new MorceauMath(((MorceauMath)n).Texte), n));
        Assert.Equal(texte, string.Concat(noeuds.Cast<MorceauMath>().Select(n => n.Texte)));
    }

    [Fact]
    public void Html_du_rapport()
    {
        var html = EcritureMath.Html("ΔU = b × (ρ1 × L / S × cos φ) × Ib");
        Assert.Contains("class=\"fr\"", html);
        Assert.Contains("<i>I</i><sub>b</sub>", html);
        Assert.Contains("<i>ρ</i><sub>1</sub>", html);
    }

    [Fact]
    public void Courant_moteur_avec_les_valeurs()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());
        var ligne = r.Departs.SelectMany(d => d.Note.Lignes).First(l => l.Code == CodesCalcul.CourantEmploi && l.Formule.StartsWith("I = P / (√3"));
        Assert.Matches(@"^I = P / \(√3 × U[^=]*\) = \d+ / \(√3 × 400 × [\d,]+.*\) = [\d,]+ A", ligne.Formule);
    }
}

public class CommandeVentilationTests
{
    static ResultatProjet Ventile(Action<ConfigurationArmoire> reglage, double puissance = 500)
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.Forcages[CalculThermique.CodePuissance] = Version3Tests.Force(puissance);
        reglage(projet.Armoire);
        return CalculProjet.Calculer(projet);
    }

    [Fact]
    public void Thermostat_par_defaut_regle_a_la_consigne()
    {
        var r = Ventile(c => c.ConsigneThermostat = 32);
        var bt = Assert.Single(r.Arbre.Parcourir(), e => e.RepereOrigine == "BT-EV");
        Assert.StartsWith("réglé à 32 °C", bt.Calibre);
    }

    [Fact]
    public void Marche_continue_sans_thermostat()
    {
        var r = Ventile(c => c.CommandeVentilation = CommandeVentilation.Continue);
        Assert.DoesNotContain(r.Arbre.Parcourir(), e => e.RepereOrigine == "BT-EV");
        var q = Assert.Single(r.Arbre.Parcourir(), e => e.RepereOrigine == "Q-EV");
        Assert.Contains(q.Enfants, e => e.Famille == "Ventilation");
        Assert.Contains(r.Thermique!.Note.Lignes, l => l.Message.Contains("Marche continue"));
    }

    [Theory]
    [InlineData(PositionVentilation.CoteGauche, "paroi gauche")]
    [InlineData(PositionVentilation.CoteDroit, "paroi droite")]
    [InlineData(PositionVentilation.Porte, "en porte")]
    public void Position_choisie(PositionVentilation position, string texte)
    {
        // 300 W : un ventilateur de 300 m³/h tient sur un côté de 300 mm de profondeur.
        var r = Ventile(c => c.PositionVentilation = position, 300);
        Assert.Equal(position, r.Thermique!.PositionVentilation);
        Assert.Contains(texte, Assert.Single(r.Arbre.Parcourir(), e => e.Famille == "Ventilation").Designation);
    }

    [Fact]
    public void Cote_trop_etroit_reporte_en_porte()
    {
        var r = Ventile(c => c.PositionVentilation = PositionVentilation.CoteGauche);
        Assert.Equal(PositionVentilation.Porte, r.Thermique!.PositionVentilation);
        Assert.Contains(r.Thermique.Note.Lignes, l => l.Libelle == "Paroi des ventilateurs" && l.Statut == StatutCalcul.Alerte);
        var armoire = new ResultatArmoire { Note = new NoteDeCalcul("T"), Largeur = 600, Hauteur = 800, Profondeur = 150, Cellules = 1 };
        Assert.Null(CalculThermique.ChoisirVentilateur(150, armoire, Core.Normes.CatalogueVentilateurs.Actif, PositionVentilation.CoteGauche));
        Assert.NotNull(CalculThermique.ChoisirVentilateur(150, armoire, Core.Normes.CatalogueVentilateurs.Actif));
    }

    [Fact]
    public void Ventilateur_impose()
    {
        var r = Ventile(c => c.VentilateurImpose = "NSYCVF850M230PF");
        Assert.Equal("NSYCVF850M230PF", r.Thermique!.ModeleVentilateur!.Reference);
    }
}

public class TensionVentilationTests
{
    [Fact]
    public void Ventilateurs_24V_sur_l_alimentation_et_au_bilan()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.Forcages[CalculThermique.CodePuissance] = Version3Tests.Force(300);
        var avant = CalculProjet.Calculer(projet).Consommation24V;
        projet.Armoire.TensionVentilation = TensionVentilation.Continu24;
        var r = CalculProjet.Calculer(projet);

        var m = r.Thermique!.ModeleVentilateur!;
        Assert.True(m.EnContinu24V);
        Assert.Contains(r.Consommateurs24V, c => c.Designation.StartsWith($"Ventilateurs {m.Reference}"));
        Assert.Equal(avant + r.Thermique.NombreVentilateurs * m.Puissance / 24, r.Consommation24V, 3);
        var q = Assert.Single(r.Arbre.Parcourir(), e => e.RepereOrigine == "Q-EV");
        Assert.Contains("24 V DC", q.Calibre);
        Assert.Contains(r.Arbre.Parcourir(), e => e.Famille == "Alimentation" && e.Enfants.Contains(q));
    }

    [Fact]
    public void Repli_en_230V_si_aucun_24V_ne_suffit()
    {
        var projet = Version3Tests.ProjetMachine();
        projet.Armoire.Forcages[CalculThermique.CodePuissance] = Version3Tests.Force(900);
        projet.Armoire.PositionVentilation = PositionVentilation.Porte;
        projet.Armoire.TensionVentilation = TensionVentilation.Continu24;
        var r = CalculProjet.Calculer(projet);
        if (r.Thermique!.ModeleVentilateur is { } m && !m.EnContinu24V)
            Assert.Contains(r.Thermique.Note.Lignes, l => l.Libelle == "Tension des ventilateurs");
    }
}
