using System.Globalization;
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
        base.OnStartup(e);
    }
}
