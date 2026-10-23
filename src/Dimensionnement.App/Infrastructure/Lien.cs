using System.Windows.Data;

namespace Dimensionnement.App.Infrastructure;

/// <summary>
/// Liaison bidirectionnelle vers le modèle qui signale chaque modification (événement SourceUpdated),
/// pour relancer le calcul du projet. Les nombres acceptent la virgule comme le point ; une valeur hors des usages
/// met la case en rouge avec son explication et peut être forcée.
/// </summary>
public class Lien : Binding
{
    public Lien() => Initialiser();

    public Lien(string chemin) : base(chemin)
    {
        Initialiser();
        ValidationRules.Add(new RegleVraisemblance(chemin));
    }

    void Initialiser()
    {
        Mode = BindingMode.TwoWay;
        NotifyOnSourceUpdated = true;
        NotifyOnValidationError = true;
        ValidatesOnExceptions = true;
        Converter = ConvertisseurSaisie.Instance;
    }
}
