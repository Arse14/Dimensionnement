using System.Windows;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.Vues;

/// <summary>Question posée quand une source 24 V DC manque : alimentation à découpage ou transformateur + redresseur.</summary>
public partial class FenetreChoixSource : Window
{
    public FenetreChoixSource(string raison)
    {
        InitializeComponent();
        Raison.Text = raison;
    }

    /// <summary>Technologie choisie ; null si l'utilisateur ne veut rien ajouter.</summary>
    public SourceAlimentation? Choix { get; private set; }

    /// <summary>Vrai si le choix doit devenir la préférence du projet.</summary>
    public bool Retenu => Retenir.IsChecked == true;

    void Alimentation(object sender, RoutedEventArgs e) => Fermer(SourceAlimentation.Decoupage);
    void Transformateur(object sender, RoutedEventArgs e) => Fermer(SourceAlimentation.Transformateur);
    void Annuler(object sender, RoutedEventArgs e) => Fermer(null);

    void Fermer(SourceAlimentation? choix)
    {
        Choix = choix;
        DialogResult = choix is not null;
    }
}
