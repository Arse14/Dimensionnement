using System.Globalization;
using System.Windows;
using Dimensionnement.Core.Calculs;

namespace Dimensionnement.App.Vues;

/// <summary>Saisie d'une valeur forcée et de son motif.</summary>
public partial class FenetreForcage : Window
{
    public FenetreForcage(LigneCalcul ligne)
    {
        InitializeComponent();
        Libelle.Text = ligne.Libelle;
        Calcul.Text = $"Valeur calculée : {ligne.ValeurCalculee:0.##} {ligne.Unite} ; valeur retenue : {ligne.ValeurTexte}";
        SaisieValeur.Text = ligne.ValeurRetenue?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        Loaded += (_, _) => { SaisieValeur.Focus(); SaisieValeur.SelectAll(); };
    }

    public double Valeur { get; private set; }
    public string Motif { get; private set; } = "";

    void Valider(object sender, RoutedEventArgs e)
    {
        var texte = SaisieValeur.Text.Replace('.', ',');
        if (!double.TryParse(texte, NumberStyles.Float, CultureInfo.GetCultureInfo("fr-FR"), out var valeur) || valeur <= 0)
        {
            Erreur.Text = "Saisissez une valeur numérique positive.";
            return;
        }
        if (string.IsNullOrWhiteSpace(SaisieMotif.Text))
        {
            Erreur.Text = "Le motif est obligatoire : il apparaît dans la note de calcul.";
            return;
        }
        Valeur = valeur;
        Motif = SaisieMotif.Text.Trim();
        DialogResult = true;
    }
}
