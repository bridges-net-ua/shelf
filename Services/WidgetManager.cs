using System;
using System.Collections.Generic;
using System.Linq;
using Shelf.Models;
using Shelf.Sdk;

namespace Shelf.Services;

public class WidgetManager
{
    public event Action? ActiveWidgetsChanged;

    private readonly Dictionary<string, IWidget> _instances = new();

    public void Sync()
    {
        var list = App.Settings.Current.Widgets;

        // Migration: ensure InstanceId and TypeId are set on each entry
        foreach (var entry in list)
        {
            if (string.IsNullOrEmpty(entry.TypeId) && !string.IsNullOrEmpty(entry.Id))
                entry.TypeId = entry.Id;
            if (string.IsNullOrEmpty(entry.InstanceId))
                entry.InstanceId = Guid.NewGuid().ToString("N");
            entry.Id = null; // drop legacy field after migration
        }

        // First-time defaults: one instance of every registered widget type
        if (list.Count == 0 && !App.Settings.Current.InitializedWithDefaults)
        {
            foreach (var type in WidgetRegistry.Types)
            {
                list.Add(new WidgetEntry
                {
                    InstanceId = Guid.NewGuid().ToString("N"),
                    TypeId = type.TypeId,
                    Enabled = true,
                    State = ""
                });
            }
            App.Settings.Current.InitializedWithDefaults = true;
        }

        // Note: entries whose TypeId is no longer in the registry are kept in settings.json
        // (so that user data survives temporary plugin uninstall), but skipped when creating instances.

        // Create an instance per entry that has a registered type
        _instances.Clear();
        foreach (var entry in list)
        {
            var typeInfo = WidgetRegistry.FindType(entry.TypeId);
            if (typeInfo == null) continue;
            try
            {
                var instance = typeInfo.Create();
                _instances[entry.InstanceId] = instance;
            }
            catch
            {
                // skip instances that fail to create
            }
        }
    }

    public void LoadStates()
    {
        foreach (var entry in App.Settings.Current.Widgets)
        {
            if (!_instances.TryGetValue(entry.InstanceId, out var widget)) continue;
            if (string.IsNullOrEmpty(entry.State)) continue;
            try { widget.LoadState(entry.State); } catch { }
        }
    }

    public void SaveStates()
    {
        foreach (var entry in App.Settings.Current.Widgets)
        {
            if (!_instances.TryGetValue(entry.InstanceId, out var widget)) continue;
            try { entry.State = widget.SaveState(); } catch { }
        }
        App.Settings.Save();
    }

    public IEnumerable<(WidgetEntry Entry, IWidget Widget)> GetActiveOrderedWithEntries()
    {
        foreach (var entry in App.Settings.Current.Widgets)
        {
            if (!entry.Enabled) continue;
            if (!_instances.TryGetValue(entry.InstanceId, out var widget)) continue;
            yield return (entry, widget);
        }
    }

    public IEnumerable<(WidgetEntry Entry, IWidget Widget)> GetAllWithEntries()
    {
        foreach (var entry in App.Settings.Current.Widgets)
        {
            if (!_instances.TryGetValue(entry.InstanceId, out var widget)) continue;
            yield return (entry, widget);
        }
    }

    public IWidget? GetInstance(string instanceId) =>
        _instances.TryGetValue(instanceId, out var w) ? w : null;

    public string AddInstance(string typeId)
    {
        var typeInfo = WidgetRegistry.FindType(typeId);
        if (typeInfo == null) return "";

        IWidget instance;
        try { instance = typeInfo.Create(); }
        catch { return ""; }

        var instanceId = Guid.NewGuid().ToString("N");
        _instances[instanceId] = instance;

        App.Settings.Current.Widgets.Add(new WidgetEntry
        {
            InstanceId = instanceId,
            TypeId = typeId,
            Enabled = true,
            State = ""
        });
        App.Settings.Save();
        ActiveWidgetsChanged?.Invoke();
        return instanceId;
    }

    public void RemoveInstance(string instanceId)
    {
        var list = App.Settings.Current.Widgets;
        var entry = list.FirstOrDefault(e => e.InstanceId == instanceId);
        if (entry == null) return;
        list.Remove(entry);
        _instances.Remove(instanceId);
        App.Settings.Save();
        ActiveWidgetsChanged?.Invoke();
    }

    public void SetEnabled(string instanceId, bool enabled)
    {
        var entry = App.Settings.Current.Widgets.FirstOrDefault(e => e.InstanceId == instanceId);
        if (entry == null) return;
        entry.Enabled = enabled;
        App.Settings.Save();
        ActiveWidgetsChanged?.Invoke();
    }

    public void MoveUp(string instanceId)
    {
        var list = App.Settings.Current.Widgets;
        int idx = list.FindIndex(e => e.InstanceId == instanceId);
        if (idx > 0)
        {
            (list[idx - 1], list[idx]) = (list[idx], list[idx - 1]);
            App.Settings.Save();
            ActiveWidgetsChanged?.Invoke();
        }
    }

    public void MoveDown(string instanceId)
    {
        var list = App.Settings.Current.Widgets;
        int idx = list.FindIndex(e => e.InstanceId == instanceId);
        if (idx >= 0 && idx < list.Count - 1)
        {
            (list[idx], list[idx + 1]) = (list[idx + 1], list[idx]);
            App.Settings.Save();
            ActiveWidgetsChanged?.Invoke();
        }
    }

    // Move an enabled widget so that it ends up immediately before the widget
    // identified by anchorInstanceId. If anchorInstanceId is null or empty,
    // the widget is moved to be the last enabled one. Disabled widgets keep
    // their absolute positions in the full settings list.
    public void MoveBeforeEnabled(string instanceId, string? anchorInstanceId)
    {
        if (instanceId == anchorInstanceId) return;

        var list = App.Settings.Current.Widgets;
        var item = list.FirstOrDefault(e => e.InstanceId == instanceId);
        if (item == null) return;

        list.Remove(item);

        int insertAt;
        if (string.IsNullOrEmpty(anchorInstanceId))
        {
            int lastEnabled = -1;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].Enabled) { lastEnabled = i; break; }
            insertAt = lastEnabled + 1;
        }
        else
        {
            insertAt = list.FindIndex(e => e.InstanceId == anchorInstanceId);
            if (insertAt < 0) insertAt = list.Count;
        }

        if (insertAt < 0) insertAt = 0;
        if (insertAt > list.Count) insertAt = list.Count;
        list.Insert(insertAt, item);

        App.Settings.Save();
        ActiveWidgetsChanged?.Invoke();
    }

    public void NotifyOrderLockChanged() => ActiveWidgetsChanged?.Invoke();

    public void TogglePin(string instanceId)
    {
        var entry = App.Settings.Current.Widgets.FirstOrDefault(e => e.InstanceId == instanceId);
        if (entry == null) return;
        entry.Pinned = !entry.Pinned;
        App.Settings.Save();
        ActiveWidgetsChanged?.Invoke();
    }

    public bool IsPinned(string instanceId) =>
        App.Settings.Current.Widgets.FirstOrDefault(e => e.InstanceId == instanceId)?.Pinned ?? false;
}
