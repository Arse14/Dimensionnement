using System.Collections.ObjectModel;
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
    /// <summary>Scrutateur laser de sécurité (protection de zone).</summary>
    ScrutateurLaser,
    Autre,
}

/// <summary>Marque des barrières immatérielles et scrutateurs laser (Datasensing = ex-Datalogic).</summary>
public enum FabricantCapteurSecurite { Datasensing, Schneider, Sick }

/// <summary>Détection demandée à une barrière immatérielle : doigt (14 mm), main (30 mm) ou corps (multi-faisceaux).</summary>
public enum DetectionBarriere { Doigt, Main, Corps }

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

    /// <summary>Sorties de sécurité à prévoir en plus des coupures des départs (voyants, électrovannes, freins…).</summary>
    public int SortiesSupplementaires { get; set; }

    /// <summary>Numéros des départs dont la fonction coupe la puissance.</summary>
    public List<int> Departs { get; set; } = new();

    // Barrière immatérielle et scrutateur laser : montés sur la machine, hors de l'armoire.
    public FabricantCapteurSecurite FabricantCapteur { get; set; } = FabricantCapteurSecurite.Datasensing;
    public DetectionBarriere Detection { get; set; } = DetectionBarriere.Main;

    /// <summary>Hauteur protégée d'une barrière, en mm.</summary>
    public double HauteurProtegee { get; set; } = 900;

    /// <summary>Portée d'une barrière (distance émetteur – récepteur) ou rayon du champ de protection d'un scrutateur, en m.</summary>
    public double Portee { get; set; } = 3;

    /// <summary>Référence imposée de la barrière ou du scrutateur ; vide pour le choix automatique.</summary>
    public string ReferenceCapteur { get; set; } = "";

    /// <summary>Vrai pour une barrière immatérielle ou un scrutateur laser, choisis au catalogue des capteurs.</summary>
    [JsonIgnore]
    public bool CapteurOptique => Type is TypeFonctionSecurite.BarriereImmaterielle or TypeFonctionSecurite.ScrutateurLaser;

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

    public ObservableCollection<FonctionSecurite> Fonctions { get; set; } = new();
}
