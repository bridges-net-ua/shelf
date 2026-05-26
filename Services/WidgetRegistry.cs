using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Shelf.Sdk;

namespace Shelf.Services;

public class WidgetTypeInfo
{
    public string TypeId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public bool HasSettings { get; init; }
    public Type ClrType { get; init; } = typeof(object);

    public IWidget Create() => (IWidget)Activator.CreateInstance(ClrType)!;
}

public static class WidgetRegistry
{
    private static readonly List<WidgetTypeInfo> _types = new();

    public static IReadOnlyList<WidgetTypeInfo> Types => _types;

    public static void Initialize()
    {
        if (_types.Count > 0) return;

        // Force-load every Shelf.Widgets.* DLL that sits next to the host
        // executable so its IWidget types are visible to AppDomain reflection.
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        if (Directory.Exists(baseDir))
        {
            foreach (var dll in Directory.EnumerateFiles(baseDir, "Shelf.Widgets.*.dll", SearchOption.TopDirectoryOnly))
            {
                try { Assembly.LoadFrom(dll); } catch { /* ignore unloadable DLLs */ }
            }
        }

        var widgetInterface = typeof(IWidget);
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] asmTypes;
            try { asmTypes = asm.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { asmTypes = ex.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (var t in asmTypes)
            {
                if (t == null || t.IsAbstract || t.IsInterface) continue;
                if (!widgetInterface.IsAssignableFrom(t)) continue;
                if (_types.Any(x => x.ClrType == t)) continue;

                IWidget proto;
                try { proto = (IWidget)Activator.CreateInstance(t)!; }
                catch { continue; }

                if (_types.Any(x => x.TypeId == proto.Id)) continue;

                _types.Add(new WidgetTypeInfo
                {
                    TypeId = proto.Id,
                    DisplayName = proto.DisplayName,
                    Description = proto.Description,
                    HasSettings = proto.HasSettings,
                    ClrType = t
                });
            }
        }
    }

    public static WidgetTypeInfo? FindType(string typeId) =>
        _types.FirstOrDefault(t => t.TypeId == typeId);
}
