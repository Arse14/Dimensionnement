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
        TypeAlimentation.Bi400 => "Biphasé 400 V entre phases (2P)",
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
        MarqueVariateur.Projet => "Fabricant du projet",
        MateriauEnveloppe.AcierPeint => "Acier peint (k = 5,5)",
        MateriauEnveloppe.Inox => "Inox (k = 4,5)",
        MateriauEnveloppe.Aluminium => "Aluminium (k = 12)",
        MateriauEnveloppe.Polyester => "Polyester (k = 3,5)",
        InstallationEnveloppe i => Core.Armoire.CalculThermique.LibelleInstallation(i) is var l ? char.ToUpper(l[0]) + l[1..] : "",
        FabricantMateriel.Schneider => "Schneider Electric",
        MarqueFamille.Projet => "Fabricant du projet",
        MarqueFamille.Schneider => "Schneider Electric",
        MarqueAlimentation.Projet => "Selon le projet (Siemens, sinon Schneider)",
        MarqueAlimentation.Schneider => "Schneider Electric (Phaseo)",
        MarqueAlimentation.Siemens => "Siemens (SITOP)",
        UsageAlimentation24V.Commande => "Toute la commande",
        UsageAlimentation24V.Automate => "Automate et ses E/S",
        UsageAlimentation24V.Securite => "Sécurité",
        UsageAlimentation24V.Actionneurs => "Actionneurs et bobines",
        SourceAlimentation.Decoupage => "Alimentation à découpage",
        SourceAlimentation.Transformateur => "Transformateur",
        TensionCommande.Continu24 => "24 V DC",
        TensionCommande.Alternatif24 => "24 V AC",
        TensionCommande.Alternatif230 => "230 V AC",
        PreferenceSource.Demander => "Me demander à chaque fois",
        PreferenceSource.Decoupage => "Alimentation à découpage",
        PreferenceSource.Transformateur => "Transformateur + redresseur",
        GammeIhm.Standard => "Standard (Basic Panel, Harmony ST6)",
        GammeIhm.Confort => "Confort (Unified Comfort, Harmony GTO / GTU)",
        Core.Calculs.NiveauSelectivite n => Core.Calculs.CalculSelectivite.Libelle(n),
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

/// <summary>Vrai quand la carte (valeur 0 : son départ) est le départ en cours d'édition (valeur 1 : la sélection).</summary>
public class MemeDepart : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values is [Depart carte, Depart selection] && carte.Id == selection.Id;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
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
    public static MateriauEnveloppe[] Materiaux { get; } = Enum.GetValues<MateriauEnveloppe>();
    public static InstallationEnveloppe[] Installations { get; } = Enum.GetValues<InstallationEnveloppe>();
    public static FabricantMateriel[] FabricantsMateriel { get; } = Enum.GetValues<FabricantMateriel>();
    public static MarqueVariateur[] MarquesVariateur { get; } = Enum.GetValues<MarqueVariateur>();
    public static MarqueEnveloppe[] MarquesEnveloppe { get; } = Enum.GetValues<MarqueEnveloppe>();
    public static MarqueFamille[] MarquesFamille { get; } = Enum.GetValues<MarqueFamille>();
    public static MarqueAlimentation[] MarquesAlimentation { get; } = Enum.GetValues<MarqueAlimentation>();
    public static UsageAlimentation24V[] Usages24V { get; } = Enum.GetValues<UsageAlimentation24V>();
    public static SourceAlimentation[] SourcesAlimentation { get; } = Enum.GetValues<SourceAlimentation>();
    public static TensionCommande[] TensionsCommande { get; } = Enum.GetValues<TensionCommande>();
    /// <summary>Tensions d'un consommateur déclaré à la main.</summary>
    public static TensionCommande[] TensionsConsommateur { get; } = { TensionCommande.Continu24, TensionCommande.Alternatif24, TensionCommande.Alternatif230 };
    public static PreferenceSource[] PreferencesSource { get; } = Enum.GetValues<PreferenceSource>();
    public static GammeIhm[] GammesIhm { get; } = Enum.GetValues<GammeIhm>();
    public static string[] Rubriques { get; } = { "Puissance", "Commande", "Auxiliaires", "Distribution", "Sécurité" };
}

/// <summary>Image d'un fichier local (photo d'article), chargée sans verrouiller le fichier ; rien si absent ou illisible.</summary>
public class CheminImage : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string { Length: > 0 } chemin || !System.IO.File.Exists(chemin)) return null;
        try
        {
            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(chemin, UriKind.Absolute);
            if (parameter is string largeur && int.TryParse(largeur, out var l)) image.DecodePixelWidth = l;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is System.IO.IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Nom du fichier d'un chemin, ou l'adresse telle quelle pour un lien.</summary>
public class NomFichier : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is string s && !s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? System.IO.Path.GetFileName(s) : value ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible quand la valeur est fausse ou qu'une liste est vide ; masqué sinon.</summary>
public class FauxVisible : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is false or System.Collections.ICollection { Count: 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visible quand une liste contient au moins un élément.</summary>
public class NonNulVisible : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is System.Collections.ICollection { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Texte en majuscules (titres de filtres).</summary>
public class Majuscules : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string ?? "").ToUpper(culture);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
