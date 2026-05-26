using System.Windows;
using System.Windows.Controls;

namespace Shelf.Sdk;

public abstract class WidgetBase : UserControl, IWidget
{
    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public virtual string Description => "";
    public virtual string InstanceLabel => DisplayName;

    public UserControl CreateView() => this;

    public virtual bool HasSettings => false;
    public virtual void ShowSettings(Window owner) { }

    public virtual string SaveState() => "";
    public virtual void LoadState(string json) { }
}
