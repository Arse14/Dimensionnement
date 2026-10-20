using System.Windows.Data;

namespace Dimensionnement.App.Infrastructure;

/// <summary>
/// Liaison bidirectionnelle vers le modèle qui signale chaque modification (événement SourceUpdated),
/// pour relancer le calcul du projet.
/// </summary>
public class Lien : Binding
{
    public Lien() => Initialiser();

    public Lien(string chemin) : base(chemin) => Initialiser();

    void Initialiser()
    {
        Mode = BindingMode.TwoWay;
        NotifyOnSourceUpdated = true;
        ValidatesOnExceptions = true;
    }
}
