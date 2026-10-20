using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Dimensionnement.App.Infrastructure;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Signaler([CallerMemberName] string? propriete = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propriete));

    /// <summary>Signale toutes les propriétés, après un recalcul complet.</summary>
    protected void SignalerTout() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
