using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Dimensionnement.Core.Materiel;
using Dimensionnement.Core.Modele;

namespace Dimensionnement.App.Infrastructure;

/// <summary>Couleur du voyant d'état : vert conforme, orange forcé ou alerte, rouge non conforme.</summary>
public class StatutCouleur : IValueConverter
{
    static readonly Brush Info = Gel("#6E7681");
    static readonly Brush Conforme = Gel("#3FB950");
    static readonly Brush Orange = Gel("#F0A020");
    static readonly Brush Rouge = Gel("#F85149");

    static Brush Gel(string couleur)
    {
        var pinceau = new SolidColorBrush((Color)ColorConverter.ConvertFromString(couleur));
        pinceau.Freeze();
        return pinceau;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StatutCalcul.Conforme => Conforme,
        StatutCalcul.Forcee or StatutCalcul.Alerte => Orange,
        StatutCalcul.NonConforme => Rouge,
        _ => Info,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Libellé français des valeurs d'énumération affichées dans les listes.</summary>
public class LibelleEnum : IValueConverter
{
    public static string Libelle(object? valeur) => valeur switch
    {
        TypeAlimentation.Mono230 => "Monophasé 230 V (1P+N)",
        TypeAlimentation.Tri400 => "Triphasé 400 V (3P)",
        TypeAlimentation.Tetra400 => "Tétrapolaire 400 V (3P+N)",
        SchemaLiaisonTerre.TT => "TT",
        SchemaLiaisonTerre.TNC => "TN-C",
        SchemaLiaisonTerre.TNS => "TN-S",
        SchemaLiaisonTerre.IT => "IT",
        OrigineAlimentation.ReseauPublicBT => "Réseau public BT (ΔU 5 %)",
        OrigineAlimentation.PostePrive => "Poste privé HTA/BT (ΔU 8 %)",
        ModeDemarrage.Direct => "Direct",
        ModeDemarrage.Inverseur => "Inverseur de sens",
        ModeDemarrage.EtoileTriangle => "Étoile-triangle",
        ModeDemarrage.Variateur => "Variateur de fréquence",
        ModeDemarrage.DemarreurProgressif => "Démarreur progressif",
        TypeProtection.Disjoncteur => "Disjoncteur",
        TypeProtection.Fusibles => "Sectionneur porte-fusibles",
        TypeFonctionSecurite.ArretUrgence => "Arrêt d'urgence",
        TypeFonctionSecurite.ProtecteurMobile => "Protecteur mobile (porte)",
        TypeFonctionSecurite.BarriereImmaterielle => "Barrière immatérielle",
        TypeFonctionSecurite.CommandeBimanuelle => "Commande bimanuelle",
        TypeFonctionSecurite.ArretSurCouple => "STO variateur",
        TypeFonctionSecurite.Autre => "Autre fonction",
        NiveauPerformance p => $"PLr {p}",
        MarqueAutomate.Siemens => "Siemens (S7-1200, S7-1500)",
        MarqueAutomate.Schneider => "Schneider Electric (Modicon)",
        MethodePose.A1 => "A1 – conduit encastré dans paroi isolante",
        MethodePose.B1 => "B1 – conduit ou goulotte sur paroi",
        MethodePose.C => "C – câble fixé sur paroi",
        MethodePose.E => "E – chemin de câbles perforé",
        Isolant.PVC => "PVC (70 °C)",
        Isolant.PR => "PR / EPR (90 °C)",
        CourbeDisjoncteur c => $"Courbe {c}",
        SensibiliteDifferentiel.Aucun => "Sans différentiel",
        SensibiliteDifferentiel.Ma30 => "30 mA",
        SensibiliteDifferentiel.Ma300 => "300 mA",
        ZoneCem.Sale => "Zone CEM « sale »",
        ZoneCem.Intermediaire => "Zone CEM intermédiaire",
        ZoneCem.Propre => "Zone CEM « propre »",
        ZoneCem.HorsArmoire => "Hors armoire",
        null => "",
        _ => valeur.ToString() ?? "",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Libelle(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Masque un élément dont la valeur est vide ou nulle.</summary>
public class VideMasque : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Listes de choix proposées dans les formulaires.</summary>
public static class Listes
{
    public static TypeAlimentation[] Alimentations { get; } = Enum.GetValues<TypeAlimentation>();
    public static SchemaLiaisonTerre[] Slt { get; } = Enum.GetValues<SchemaLiaisonTerre>();
    public static OrigineAlimentation[] Origines { get; } = Enum.GetValues<OrigineAlimentation>();
    public static ModeDemarrage[] Demarrages { get; } = Enum.GetValues<ModeDemarrage>();
    public static MethodePose[] Poses { get; } = Enum.GetValues<MethodePose>();
    public static Isolant[] Isolants { get; } = Enum.GetValues<Isolant>();
    public static CourbeDisjoncteur[] Courbes { get; } = Enum.GetValues<CourbeDisjoncteur>();
    public static SensibiliteDifferentiel[] Differentiels { get; } = Enum.GetValues<SensibiliteDifferentiel>();
    public static TypeProtection[] Protections { get; } = Enum.GetValues<TypeProtection>();
    public static FabricantSecurite[] FabricantsSecurite { get; } = Enum.GetValues<FabricantSecurite>();
    public static TypeFonctionSecurite[] TypesFonction { get; } = Enum.GetValues<TypeFonctionSecurite>();
    public static NiveauPerformance[] NiveauxPerformance { get; } = Enum.GetValues<NiveauPerformance>();
    public static MarqueAutomate[] MarquesAutomate { get; } = Enum.GetValues<MarqueAutomate>();
    public static string[] Rubriques { get; } = { "Puissance", "Commande", "Auxiliaires", "Distribution", "Sécurité" };
}
