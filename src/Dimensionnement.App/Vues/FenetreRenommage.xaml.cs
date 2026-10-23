using System.Windows;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.Vues;

/// <summary>Saisie du repère et de la désignation qu'un appareil portera à la place du nom calculé.</summary>
public partial class FenetreRenommage : Window
{
    public FenetreRenommage(ElementMateriel e)
    {
        InitializeComponent();
        Origine.Text = $"{e.RepereOrigine} – {e.Famille}";
        SaisieRepere.Text = e.Repere;
        SaisieDesignation.Text = e.Designation;
        Loaded += (_, _) => { SaisieRepere.Focus(); SaisieRepere.SelectAll(); };
    }

    /// <summary>Nom retenu ; null pour revenir au nom calculé.</summary>
    public Renommage? Resultat { get; private set; }

    void Valider(object sender, RoutedEventArgs e)
    {
        Resultat = new Renommage { Repere = SaisieRepere.Text.Trim(), Designation = SaisieDesignation.Text.Trim() };
        DialogResult = true;
    }

    void Retablir(object sender, RoutedEventArgs e)
    {
        Resultat = null;
        DialogResult = true;
    }
}
