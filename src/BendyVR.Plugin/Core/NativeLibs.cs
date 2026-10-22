using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BendyVR.Core;

/// <summary>Maps DllImport library names to absolute paths (one resolver per assembly is allowed).</summary>
public static class NativeLibs
{
    private static readonly Dictionary<string, string> Paths = new(StringComparer.OrdinalIgnoreCase);
    private static bool _installed;

    public static void Register(string name, string fullPath)
    {
        Paths[name] = fullPath;
        if (_installed) return;
        _installed = true;
        NativeLibrary.SetDllImportResolver(Assembly.GetExecutingAssembly(), (lib, _, _) =>
            Paths.TryGetValue(lib, out var p) ? NativeLibrary.Load(p) : IntPtr.Zero);
    }
}
