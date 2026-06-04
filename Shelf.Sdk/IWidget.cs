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

    // Called on the UI thread after the UI language changes at runtime (Loc.Apply).
    // Widgets that render any text imperatively in code (dates via Loc.Culture,
    // weather descriptions, holiday day labels, error lines, etc.) override this to
    // re-read their strings and repaint. Content bound purely via {DynamicResource ...}
    // in XAML updates on its own, so such widgets can leave the default no-op.
    void OnLanguageChanged() { }

    UserControl CreateView();

    bool HasSettings { get; }
    void ShowSettings(Window owner);

    string SaveState();
    void LoadState(string json);
}
