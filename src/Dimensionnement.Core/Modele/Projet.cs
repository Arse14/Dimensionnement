using System.Collections.ObjectModel;

namespace Dimensionnement.Core.Modele;

public class Projet
{
    /// <summary>Version du format de fichier, pour les migrations futures.</summary>
    public int VersionFormat { get; set; } = 1;

    public string Nom { get; set; } = "Nouveau projet";
    public string Affaire { get; set; } = "";
    public string Client { get; set; } = "";
    public string Auteur { get; set; } = "";

    /// <summary>Indice de révision (A, B, C…).</summary>
    public string Indice { get; set; } = "A";
    public DateTime Creation { get; set; } = DateTime.Now;
    public DateTime Modification { get; set; } = DateTime.Now;

    public Reseau Reseau { get; set; } = new();
    public List<Depart> Departs { get; set; } = new();
    public ConfigurationSecurite Securite { get; set; } = new();
    public ConfigurationAutomate Automate { get; set; } = new();
    public List<Revision> Revisions { get; set; } = new();

    /// <summary>Points de la liste de contrôle CEM / basse tension déjà vérifiés, par identifiant.</summary>
    public Dictionary<string, bool> Controles { get; set; } = new();

    /// <summary>Consommateurs 24 V DC déclarés à la main, en plus de ceux que le projet déduit.</summary>
    public ObservableCollection<Consommateur24V> Consommateurs24V { get; set; } = new();

    /// <summary>Valeurs forcées de l'arrivée (simultanéité, calibres de tête, section d'arrivée).</summary>
    public Dictionary<string, Forcage> ForcagesArrivee { get; set; } = new();

    /// <summary>Ajoute un départ en lui attribuant le prochain numéro libre.</summary>
    public T Ajouter<T>(T depart) where T : Depart
    {
        depart.Numero = Departs.Count == 0 ? 1 : Departs.Max(d => d.Numero) + 1;
        Departs.Add(depart);
        return depart;
    }
}

public class Revision
{
    public string Indice { get; set; } = "";
    public DateTime Date { get; set; }
    public string Auteur { get; set; } = "";
    public string Description { get; set; } = "";
}
