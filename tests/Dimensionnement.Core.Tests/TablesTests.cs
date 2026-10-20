using Dimensionnement.Core.Modele;
using Dimensionnement.Core.Normes;

namespace Dimensionnement.Core.Tests;

public class TablesTests
{
    [Theory]
    [InlineData(Isolant.PVC, 30, 1.00)]
    [InlineData(Isolant.PVC, 40, 0.87)]
    [InlineData(Isolant.PR, 45, 0.87)]
    [InlineData(Isolant.PVC, 42.5, 0.83)]
    public void Facteur_de_temperature(Isolant isolant, double t, double attendu) =>
        Assert.Equal(attendu, TablesCables.FacteurTemperature(isolant, t), 2);

    [Theory]
    [InlineData(MethodePose.B1, 3, 0.70)]
    [InlineData(MethodePose.C, 2, 0.85)]
    [InlineData(MethodePose.E, 20, 0.72)]
    [InlineData(MethodePose.B1, 20, 0.38)]
    public void Facteur_de_groupement(MethodePose pose, int circuits, double attendu) =>
        Assert.Equal(attendu, TablesCables.FacteurGroupement(pose, circuits));

    [Theory]
    [InlineData(10, 10)]
    [InlineData(25, 16)]
    [InlineData(95, 50)]
    public void Section_du_PE(double phase, double pe) => Assert.Equal(pe, TablesCables.SectionPe(phase));

    [Fact]
    public void Calibre_superieur_et_plage_moteur()
    {
        Assert.Equal(16, Calibres.Superieur(Calibres.Disjoncteurs, 13.1));
        Assert.Equal(13, Calibres.Superieur(Calibres.Disjoncteurs, 13));
        Assert.Null(Calibres.Superieur(Calibres.Disjoncteurs, 700));
        Assert.Equal((13, 18), Calibres.PlageMoteur(14.5));
        Assert.Null(Calibres.PlageMoteur(95));
    }
}
