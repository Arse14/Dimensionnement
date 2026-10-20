using System.Text.Json;
using System.Text.Json.Serialization;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.Core.Persistance;

/// <summary>Enregistrement d'un projet dans un fichier unique (.dimelec, contenu JSON lisible).</summary>
public static class FichierProjet
{
    public const string Extension = ".dimelec";

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialiser(Projet projet) => JsonSerializer.Serialize(projet, Options);

    public static Projet Deserialiser(string json) =>
        JsonSerializer.Deserialize<Projet>(json, Options)
        ?? throw new InvalidDataException("Fichier projet vide ou illisible.");

    public static void Enregistrer(Projet projet, string chemin)
    {
        projet.Modification = DateTime.Now;
        // Écriture dans un fichier temporaire puis remplacement, pour ne jamais laisser un projet à moitié écrit.
        var temporaire = chemin + ".tmp";
        File.WriteAllText(temporaire, Serialiser(projet));
        File.Move(temporaire, chemin, overwrite: true);
    }

    public static Projet Ouvrir(string chemin) => Deserialiser(File.ReadAllText(chemin));
}
