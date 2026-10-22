using System.Security.Cryptography;
using System.Text.Json;

namespace Dimensionnement.Core.Persistance;

/// <summary>Nouvelle version publiée sur GitHub : son numéro, ses notes et le fichier à télécharger pour ce poste.</summary>
public record VersionDisponible(Version Version, string Etiquette, string Notes, string Fichier, string Url, long Taille, string? Sha256);

/// <summary>
/// Mise à jour depuis les releases du dépôt public : la dernière release publiée (ni brouillon ni préversion) est comparée
/// à la version de l'application. Installée, on télécharge l'installateur ; portable, l'archive zip.
/// </summary>
public static class MiseAJour
{
    public const string Depot = "Arse14/Dimensionnement";
    public const string UrlDerniere = $"https://api.github.com/repos/{Depot}/releases/latest";
    public const string UrlReleases = $"https://github.com/{Depot}/releases";

    /// <summary>« v1.2 », « V1.2.3 », « 1.2 » → 1.2.0 ; null si l'étiquette n'est pas un numéro de version.</summary>
    public static Version? Lire(string etiquette)
    {
        var texte = etiquette.Trim().TrimStart('v', 'V');
        var tiret = texte.IndexOfAny(new[] { '-', '+' });
        if (tiret >= 0) texte = texte[..tiret];
        return Version.TryParse(texte.Contains('.') ? texte : texte + ".0", out var v) ? Normaliser(v) : null;
    }

    /// <summary>Compare sur trois chiffres : 1.0 = 1.0.0 = 1.0.0.0.</summary>
    public static Version Normaliser(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>La release si elle est plus récente que <paramref name="actuelle"/> et porte le fichier attendu, sinon null.</summary>
    public static VersionDisponible? Analyser(string json, Version actuelle, bool installee)
    {
        using var doc = JsonDocument.Parse(json);
        var racine = doc.RootElement;
        if (racine.TryGetProperty("draft", out var brouillon) && brouillon.GetBoolean()) return null;
        if (racine.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        var etiquette = racine.GetProperty("tag_name").GetString() ?? "";
        if (Lire(etiquette) is not { } version || version <= Normaliser(actuelle)) return null;

        var suffixe = installee ? "-installation.exe" : "-portable-windows-x64.zip";
        foreach (var a in racine.GetProperty("assets").EnumerateArray())
        {
            var nom = a.GetProperty("name").GetString() ?? "";
            if (!nom.EndsWith(suffixe, StringComparison.OrdinalIgnoreCase)) continue;
            // GitHub publie l'empreinte SHA-256 de chaque fichier : le téléchargement est vérifié avant d'être lancé.
            var empreinte = a.TryGetProperty("digest", out var d) && d.GetString() is { } s && s.StartsWith("sha256:") ? s[7..] : null;
            return new VersionDisponible(version, etiquette, racine.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                nom, a.GetProperty("browser_download_url").GetString() ?? "", a.GetProperty("size").GetInt64(), empreinte);
        }
        return null;
    }

    /// <summary>Interroge GitHub ; null si pas de nouveauté ou pas de réseau (la vérification ne doit jamais gêner).</summary>
    public static async Task<VersionDisponible?> Verifier(HttpClient http, Version actuelle, bool installee, CancellationToken annulation = default)
    {
        try
        {
            using var requete = new HttpRequestMessage(HttpMethod.Get, UrlDerniere);
            requete.Headers.UserAgent.ParseAdd("Dimensionnement-ITP");
            requete.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var reponse = await http.SendAsync(requete, annulation);
            if (!reponse.IsSuccessStatusCode) return null;
            return Analyser(await reponse.Content.ReadAsStringAsync(annulation), actuelle, installee);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Télécharge le fichier dans <paramref name="dossier"/> en signalant l'avancement (0 à 1), puis contrôle taille et empreinte.</summary>
    public static async Task<string> Telecharger(HttpClient http, VersionDisponible v, string dossier, IProgress<double>? avancement,
        CancellationToken annulation = default)
    {
        Directory.CreateDirectory(dossier);
        var chemin = Path.Combine(dossier, v.Fichier);
        using var requete = new HttpRequestMessage(HttpMethod.Get, v.Url);
        requete.Headers.UserAgent.ParseAdd("Dimensionnement-ITP");
        using var reponse = await http.SendAsync(requete, HttpCompletionOption.ResponseHeadersRead, annulation);
        reponse.EnsureSuccessStatusCode();
        await using (var source = await reponse.Content.ReadAsStreamAsync(annulation))
        await using (var cible = File.Create(chemin))
        {
            var tampon = new byte[81920];
            long recu = 0;
            int lu;
            while ((lu = await source.ReadAsync(tampon, annulation)) > 0)
            {
                await cible.WriteAsync(tampon.AsMemory(0, lu), annulation);
                recu += lu;
                if (v.Taille > 0) avancement?.Report((double)recu / v.Taille);
            }
        }
        if (v.Taille > 0 && new FileInfo(chemin).Length != v.Taille)
            throw new InvalidDataException("Téléchargement incomplet : relance la mise à jour.");
        if (v.Sha256 is { } attendue)
        {
            await using var f = File.OpenRead(chemin);
            var obtenue = Convert.ToHexString(await SHA256.HashDataAsync(f, annulation));
            if (!obtenue.Equals(attendue, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Le fichier téléchargé ne correspond pas à celui publié sur GitHub : mise à jour annulée.");
        }
        return chemin;
    }
}
