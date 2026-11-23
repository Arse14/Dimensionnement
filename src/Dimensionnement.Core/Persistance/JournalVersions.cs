using System.Globalization;
using System.Reflection;

namespace Dimensionnement.Core.Persistance;

/// <summary>Une version et ce qu'elle apporte.</summary>
public record VersionPubliee(Version Version, string Etiquette, DateTime? Date, IReadOnlyList<string> Changements);

/// <summary>
/// Historique des versions, tiré de « nouveautes.md » embarqué : une section « ## vX.Y – JJ/MM/AAAA » par version, une ligne
/// « - » par changement. Le même fichier donne le texte de la release GitHub.
/// </summary>
public static class JournalVersions
{
    static readonly Lazy<IReadOnlyList<VersionPubliee>> Embarque = new(() => Lire(TexteEmbarque()));

    public static IReadOnlyList<VersionPubliee> Versions => Embarque.Value;

    public static string TexteEmbarque()
    {
        using var flux = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dimensionnement.Core.Persistance.nouveautes.md")!;
        return new StreamReader(flux).ReadToEnd();
    }

    public static IReadOnlyList<VersionPubliee> Lire(string texte)
    {
        var versions = new List<VersionPubliee>();
        string? etiquette = null;
        DateTime? date = null;
        var changements = new List<string>();
        void Clore()
        {
            if (etiquette is not null && MiseAJour.Lire(etiquette) is { } v) versions.Add(new VersionPubliee(v, etiquette, date, changements.ToList()));
            changements.Clear();
        }
        foreach (var brute in texte.Split('\n'))
        {
            var ligne = brute.Trim();
            if (ligne.StartsWith("## "))
            {
                Clore();
                var parties = ligne[3..].Split(new[] { '–', '-' }, 2, StringSplitOptions.TrimEntries);
                etiquette = parties[0];
                date = parties.Length > 1 && DateTime.TryParseExact(parties[1], "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                    ? d : null;
            }
            else if (ligne.StartsWith("- ") && etiquette is not null) changements.Add(ligne[2..]);
        }
        Clore();
        return versions.OrderByDescending(v => v.Version).ToList();
    }

    /// <summary>
    /// Vrai si l'application vient de passer à une version plus récente que la dernière vue, ou si la page n'a jamais été montrée
    /// (première installation, ou mise à jour depuis une version qui ne la connaissait pas) : la page s'ouvre alors d'elle-même.
    /// </summary>
    public static bool NouvelleDepuis(string? derniereVue, Version actuelle) =>
        derniereVue is null || MiseAJour.Lire(derniereVue) is not { } avant || MiseAJour.Normaliser(actuelle) > avant;
}
