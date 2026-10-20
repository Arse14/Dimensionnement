using System.Text.Json;

namespace Dimensionnement.Core.Persistance;

/// <summary>Liste des derniers projets ouverts, conservée dans le profil de l'utilisateur.</summary>
public class ProjetsRecents
{
    const int Maximum = 15;
    readonly string _fichier;

    public ProjetsRecents(string? dossier = null)
    {
        dossier ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement");
        Directory.CreateDirectory(dossier);
        _fichier = Path.Combine(dossier, "recents.json");
    }

    public IReadOnlyList<string> Lire()
    {
        try
        {
            return File.Exists(_fichier)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_fichier)) ?? new()
                : new List<string>();
        }
        catch (JsonException)
        {
            return new List<string>();
        }
    }

    public void Ajouter(string chemin)
    {
        var liste = Lire().Where(c => !string.Equals(c, chemin, StringComparison.OrdinalIgnoreCase)).ToList();
        liste.Insert(0, chemin);
        File.WriteAllText(_fichier, JsonSerializer.Serialize(liste.Take(Maximum)));
    }
}
