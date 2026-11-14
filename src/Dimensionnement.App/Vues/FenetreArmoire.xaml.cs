using System.Windows;
using Dimensionnement.App.ModelesVue;

namespace Dimensionnement.App.Vues;

/// <summary>Vue d'armoire en grand, non modale : elle suit les recalculs du projet.</summary>
public partial class FenetreArmoire : Window
{
    public FenetreArmoire(ModeleVuePrincipal modele)
    {
        InitializeComponent();
        DataContext = modele;
    }
}
