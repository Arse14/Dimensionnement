using System.Text.Json.Serialization;

namespace Dimensionnement.Core.Modele;

public enum TypeFonctionSecurite
{
    ArretUrgence,
    ProtecteurMobile,
    BarriereImmaterielle,
    CommandeBimanuelle,
    /// <summary>Suppression sûre du couple (STO) d'un variateur.</summary>
    ArretSurCouple,
    Autre,
}

/// <summary>Niveau de performance requis PLr (NF EN ISO 13849-1).</summary>
public enum NiveauPerformance { a, b, c, d, e }

public enum FabricantSecurite { Pilz, Schmersal, Siemens, Schneider }

/// <summary>Une fonction de sécurité de la machine et les départs qu'elle coupe.</summary>
public class FonctionSecurite
{
    public string Nom { get; set; } = "Arrêt d'urgence";
    public TypeFonctionSecurite Type { get; set; } = TypeFonctionSecurite.ArretUrgence;
    public NiveauPerformance PLr { get; set; } = NiveauPerformance.d;

    /// <summary>Nombre de capteurs ou de boutons raccordés (boutons d'arrêt d'urgence, interrupteurs de porte…).</summary>
    public int NombreCapteurs { get; set; } = 1;

    /// <summary>Numéros des départs dont la fonction coupe la puissance.</summary>
    public List<int> Departs { get; set; } = new();

    /// <summary>Saisie des départs commandés sous forme de texte « 1, 3, 4 ».</summary>
    [JsonIgnore]
    public string DepartsTexte
    {
        get => string.Join(", ", Departs);
        set => Departs = value
            .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => int.TryParse(t.Trim().TrimStart('D', 'd'), out var n) ? n : (int?)null)
            .Where(n => n is > 0)
            .Select(n => n!.Value)
            .Distinct()
            .ToList();
    }
}

public class ConfigurationSecurite
{
    public FabricantSecurite Fabricant { get; set; } = FabricantSecurite.Pilz;

    /// <summary>Impose un automate de sécurité même pour peu de fonctions.</summary>
    public bool ImposerAutomate { get; set; }

    public List<FonctionSecurite> Fonctions { get; set; } = new();
}
