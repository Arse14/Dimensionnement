using Dimensionnement.Core.Persistance;

namespace Dimensionnement.Core.Tests;

public class MiseAJourTests
{
    const string Release = """
        {"tag_name":"v1.1","draft":false,"prerelease":false,"body":"Alertes et mise à jour automatique",
         "assets":[
          {"name":"Dimensionnement-v1.1-installation.exe","size":1234,"digest":"sha256:ABCD","browser_download_url":"https://github.com/Arse14/Dimensionnement/releases/download/v1.1/Dimensionnement-v1.1-installation.exe"},
          {"name":"Dimensionnement-v1.1-portable-windows-x64.zip","size":999,"browser_download_url":"https://github.com/Arse14/Dimensionnement/releases/download/v1.1/Dimensionnement-v1.1-portable-windows-x64.zip"}]}
        """;

    [Theory]
    [InlineData("v1.0", "1.0.0")]
    [InlineData("V1.2.3", "1.2.3")]
    [InlineData("v2", "2.0.0")]
    [InlineData("v1.1-beta", "1.1.0")]
    public void Etiquettes_lues_comme_numeros_de_version(string etiquette, string attendue) =>
        Assert.Equal(Version.Parse(attendue), MiseAJour.Lire(etiquette));

    [Fact]
    public void Nouvelle_version_proposee_avec_le_bon_fichier()
    {
        var installee = MiseAJour.Analyser(Release, new Version(1, 0, 0, 0), installee: true)!;
        Assert.Equal(new Version(1, 1, 0), installee.Version);
        Assert.EndsWith("-installation.exe", installee.Fichier);
        Assert.Equal("ABCD", installee.Sha256);

        var portable = MiseAJour.Analyser(Release, new Version(1, 0), installee: false)!;
        Assert.EndsWith("-portable-windows-x64.zip", portable.Fichier);
        Assert.Null(portable.Sha256);
    }

    [Fact]
    public void Rien_a_proposer_si_deja_a_jour_ou_preversion()
    {
        Assert.Null(MiseAJour.Analyser(Release, new Version(1, 1, 0, 0), true));
        Assert.Null(MiseAJour.Analyser(Release, new Version(1, 2), true));
        Assert.Null(MiseAJour.Analyser(Release.Replace("\"prerelease\":false", "\"prerelease\":true"), new Version(1, 0), true));
    }
}
