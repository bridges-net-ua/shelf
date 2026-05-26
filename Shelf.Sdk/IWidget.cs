using System.Windows;
using System.Windows.Controls;

namespace Shelf.Sdk;

public interface IWidget
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }

    // Per-instance label. Defaults to DisplayName; widgets that support renaming
    // (like Notes) override this to return the user-set title.
    string InstanceLabel => DisplayName;

    UserControl CreateView();

    bool HasSettings { get; }
    void ShowSettings(Window owner);

    string SaveState();
    void LoadState(string json);
}
