using Dimensionnement.Core.Calculs;
using Dimensionnement.Core.Materiel;

namespace Dimensionnement.Core.Tests;

public class SyntheseMaterielTests
{
    [Fact]
    public void Cartes_groupees_arrivee_rubriques_puis_commande()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());

        var groupes = SyntheseMateriel.Grouper(r);

        Assert.Equal("ARRIVÉE", groupes[0].Titre);
        Assert.Equal(new[] { "W0", "QS0", "QG" }, groupes[0].Cartes.Single().Chaine.Select(m => m.Element.Repere));
        Assert.True(groupes[^1].Domaine);
        Assert.StartsWith("24 V DC – ", groupes[^1].Titre);
        Assert.Contains(groupes[^1].Cartes, c => c.Chaine.Any(m => m.Element.Repere == "API1.1"));
        Assert.Equal(r.Departs.Count, groupes.Skip(1).Where(g => !g.Domaine && !g.Titre.StartsWith("VENTILATION")).Sum(g => g.Cartes.Count));
    }

    [Fact]
    public void Chaine_principale_puis_appareil_en_parallele()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());

        // D03 inverseur : Q3, puis KM3.1 sur la ligne principale, KM3.2 en parallèle, puis le câble et le moteur.
        var d03 = SyntheseMateriel.Grouper(r).SelectMany(g => g.Cartes).Single(c => c.Depart?.Numero == 3);

        Assert.Equal(new[] { "Q3", "KM3.1", "KM3.2", "W3", "M3" }, d03.Chaine.Select(m => m.Element.Repere));
        Assert.True(d03.Chaine.Single(m => m.Element.Repere == "KM3.2").EnParallele);
    }
}

public class SchemaOrganigrammeTests
{
    [Fact]
    public void Organigramme_svg_valide_avec_tous_les_departs()
    {
        var r = CalculProjet.Calculer(Version3Tests.ProjetMachine());

        var svg = Dimensionnement.Core.Rapport.SchemaOrganigramme.Svg(r);

        var xml = System.Xml.Linq.XDocument.Parse(svg);
        Assert.Equal("svg", xml.Root!.Name.LocalName);
        foreach (var d in r.Departs) Assert.Contains($"D{d.Depart.Numero:00}", svg);
        Assert.Contains(">QG<", svg);
        Assert.Contains("24 V DC – ", svg);
        // Le domaine 24 V DC pend sous l'alimentation qui le nourrit : un bus secondaire, sans lien qui traverse le schéma.
        Assert.Contains("<circle", svg);
        Assert.DoesNotContain("<polyline", svg);
        var g5 = r.Arbre.Parcourir().First(e => e.Famille == "Alimentation");
        var api = r.Arbre.Parcourir().First(e => e.Famille == "Automate");
        double X(string repere) => double.Parse(System.Text.RegularExpressions.Regex.Match(svg,
            $"<rect x=\"([0-9.]+)\"[^>]*/><circle[^>]*/><text[^>]*>{repere}<").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(X(api.Repere) - X(g5.Repere), 0, 1200);
    }
}
