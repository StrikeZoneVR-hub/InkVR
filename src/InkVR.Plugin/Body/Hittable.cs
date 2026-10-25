using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace InkVR.Body;

/// <summary>
/// Whether something takes hits in the game (an enemy, a breakable plank, a barrel): it has a component that is an
/// IHittable. Not the level itself: the game puts a plain HittableObject on each level's [ENVIRONMENT] root so that
/// any wall or floor plays a thud when struck, which made everything in the level (every wall, floor, chair and
/// trigger volume) look hittable to a lookup through the parents.
/// Answers are kept per object: a swing asks every frame.
/// </summary>
public static class Hittable
{
    private static Il2CppSystem.Type _type;
    private static readonly Dictionary<int, bool> Known = new();

    /// <summary>On this object or one it's part of (short of the level itself).</summary>
    public static bool Is(Component c)
    {
        if (c == null) return false;
        var id = c.GetInstanceID();
        if (Known.TryGetValue(id, out var yes)) return yes;
        yes = Any(c.GetComponentsInParent<MonoBehaviour>(true));
        if (Known.Count > 4096) Known.Clear();
        Known[id] = yes;
        return yes;
    }

    /// <summary>On this object, one it's part of, or anything under it.</summary>
    public static bool InOrUnder(Transform t) => Is(t) || Any(t.GetComponentsInChildren<MonoBehaviour>(true));

    private static bool Any(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<MonoBehaviour> all)
    {
        _type ??= Il2CppType.Of<IHittable>();
        foreach (var m in all)
        {
            if (m == null) continue;
            var type = m.GetIl2CppType();
            if (!_type.IsAssignableFrom(type)) continue;
            if (m.transform.parent == null && type.Name == "HittableObject") continue; // the level's walls and floors
            return true;
        }

        return false;
    }
}
