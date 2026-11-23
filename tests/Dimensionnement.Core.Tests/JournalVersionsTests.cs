using Dimensionnement.Core.Persistance;

namespace Dimensionnement.Core.Tests;

public class JournalVersionsTests
{
    [Fact]
    public void Journal_embarque_lu_de_la_plus_recente_a_la_plus_ancienne()
    {
        var versions = JournalVersions.Versions;
        Assert.True(versions.Count >= 3);
        Assert.Equal(versions.OrderByDescending(v => v.Version).Select(v => v.Version), versions.Select(v => v.Version));
        Assert.All(versions, v => Assert.NotEmpty(v.Changements));
        var v10 = Assert.Single(versions, v => v.Etiquette == "v1.0");
        Assert.Equal(new DateTime(2026, 10, 21), v10.Date);
    }

    [Fact]
    public void Page_ouverte_une_fois_par_nouvelle_version()
    {
        Assert.True(JournalVersions.NouvelleDepuis(null, new Version(1, 3)));
        Assert.True(JournalVersions.NouvelleDepuis("v1.2", new Version(1, 3, 0, 0)));
        Assert.False(JournalVersions.NouvelleDepuis("v1.3", new Version(1, 3, 0, 0)));
    }
}
