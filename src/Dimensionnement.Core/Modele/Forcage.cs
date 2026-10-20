namespace Dimensionnement.Core.Modele;

/// <summary>Valeur imposée par l'utilisateur à la place d'une valeur calculée.</summary>
public class Forcage
{
    public double Valeur { get; set; }
    public string Motif { get; set; } = "";
    public string Auteur { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Now;
}
