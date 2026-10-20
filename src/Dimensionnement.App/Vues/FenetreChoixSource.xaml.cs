using System.Windows;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.Vues;

/// <summary>Question posée quand une source 24 V DC manque : alimentation monophasée, triphasée, réglable, ou transformateur + redresseur.</summary>
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

    /// <summary>Primaire de l'alimentation à découpage choisie.</summary>
    public TypeAlimentation Primaire { get; private set; } = TypeAlimentation.Mono230;

    /// <summary>Alimentation à sortie réglable demandée.</summary>
    public bool Reglable { get; private set; }

    void Monophasee(object sender, RoutedEventArgs e) => Fermer(SourceAlimentation.Decoupage);

    void Triphasee(object sender, RoutedEventArgs e)
    {
        Primaire = TypeAlimentation.Tri400;
        Fermer(SourceAlimentation.Decoupage);
    }

    void Reglable_(object sender, RoutedEventArgs e)
    {
        Reglable = true;
        Fermer(SourceAlimentation.Decoupage);
    }

    void Transformateur(object sender, RoutedEventArgs e) => Fermer(SourceAlimentation.Transformateur);
    void Annuler(object sender, RoutedEventArgs e) => Fermer(null);

    void Fermer(SourceAlimentation? choix)
    {
        Choix = choix;
        DialogResult = choix is not null;
    }
}
