using System.Windows;
using Dimensionnement.App.ModelesVue;

namespace Dimensionnement.App.Vues;

/// <summary>Aperçu de la demande de devis avant l'export Excel, non modal : il suit les recalculs du projet.</summary>
public partial class FenetreApercuDevis : Window
{
    public FenetreApercuDevis(ModeleVuePrincipal modele)
    {
        InitializeComponent();
        DataContext = modele;
    }
}
