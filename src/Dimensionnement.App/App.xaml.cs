using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace Dimensionnement.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Saisie et affichage des nombres à la française (virgule décimale), quel que soit le poste.
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        CultureInfo.DefaultThreadCurrentCulture = fr;
        CultureInfo.DefaultThreadCurrentUICulture = fr;
        CultureInfo.CurrentCulture = fr;
        CultureInfo.CurrentUICulture = fr;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(fr.IetfLanguageTag)));
        // Une erreur imprévue n'arrête pas l'application sans explication : elle est affichée et consignée.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Consigner(args.Exception);
        };
        base.OnStartup(e);
    }

    /// <summary>Journal des erreurs, dans le dossier des catalogues de l'utilisateur.</summary>
    public static readonly string Journal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dimensionnement", "erreurs.log");

    static void Consigner(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
            File.AppendAllText(Journal, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\n\n");
        }
        catch (Exception) { /* le journal est un confort : son échec ne doit rien bloquer */ }
        MessageBox.Show($"Une erreur imprévue s'est produite :\n\n{ex.Message}\n\nLe détail est consigné dans :\n{Journal}\n\n" +
                        "L'application continue ; enregistre ton projet par sécurité.", "Dimensionnement", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
