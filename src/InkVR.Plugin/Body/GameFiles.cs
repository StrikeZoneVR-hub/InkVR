using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace InkVR.Body;

/// <summary>
/// A reader for the game's own scene and asset files (Unity 2022.3, serialized format 22, no type trees): enough to
/// read a character model out of a chapter's files on the player's machine. The fields of each class come from
/// UnityTypes.txt (the engine's layouts for this version), read generically into dictionaries, lists and values.
/// </summary>
public sealed class GameFile
{
    public readonly string Path;
    public readonly List<string> Externals = new();
    private readonly Dictionary<long, (long Offset, uint Size, int ClassId)> _objects = new();
    private readonly byte[] _data;
    private readonly long _dataOffset;

    private GameFile(string path)
    {
        Path = path;
        _data = File.ReadAllBytes(path);
        var r = new Bin(_data) { BigEndian = true };
        r.Pos = 8;
        var version = r.U32();
        if (version < 22) throw new NotSupportedException($"serialized file version {version}");
        r.Pos += 4;
        var little = r.U8() == 0;
        r.Pos += 3;
        r.U32(); // metadata size
        r.I64(); // file size
        _dataOffset = r.I64();
        r.I64();
        r.BigEndian = !little;
        r.CString(); // unity version
        r.I32(); // platform
        if (r.U8() != 0) throw new NotSupportedException("type trees present");
        var typeCount = r.I32();
        var classIds = new int[typeCount];
        for (var i = 0; i < typeCount; i++)
        {
            var classId = r.I32();
            r.U8(); // stripped
            r.I16(); // script index
            if (classId == 114) r.Pos += 16;
            r.Pos += 16;
            classIds[i] = classId;
        }

        var count = r.I32();
        for (var i = 0; i < count; i++)
        {
            r.Align(4);
            var id = r.I64();
            var start = r.I64();
            var size = r.U32();
            var type = r.I32();
            _objects[id] = (start, size, type >= 0 && type < typeCount ? classIds[type] : -1);
        }

        var scripts = r.I32();
        for (var i = 0; i < scripts; i++)
        {
            r.I32();
            r.Align(4);
            r.I64();
        }

        var externals = r.I32();
        for (var i = 0; i < externals; i++)
        {
            r.CString();
            r.Pos += 16;
            r.I32();
            Externals.Add(r.CString());
        }
    }

    private static readonly Dictionary<string, GameFile> Open = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A file in the game's data folder, by name ("level7", "sharedassets7.assets"), read once.</summary>
    public static GameFile Get(string name)
    {
        var file = System.IO.Path.GetFileName(name.Replace('\\', '/'));
        if (Open.TryGetValue(file, out var f)) return f;
        var path = System.IO.Path.Combine(UnityEngine.Application.dataPath, file);
        if (!File.Exists(path)) return null;
        f = new GameFile(path);
        Open[file] = f;
        return f;
    }

    /// <summary>Let go of the files (they're large): after a model is built.</summary>
    public static void CloseAll() => Open.Clear();

    public int ClassOf(long pathId) => _objects.TryGetValue(pathId, out var o) ? o.ClassId : -1;

    public IEnumerable<long> ObjectsOf(int classId)
    {
        foreach (var kv in _objects)
            if (kv.Value.ClassId == classId)
                yield return kv.Key;
    }

    /// <summary>One object, read with its class's layout.</summary>
    public Dictionary<string, object> Read(long pathId)
    {
        if (!_objects.TryGetValue(pathId, out var o)) return null;
        var nodes = TypeTrees.Of(o.ClassId);
        if (nodes == null) return null;
        var r = new Bin(_data) { Pos = (int)(_dataOffset + o.Offset) };
        var i = 0;
        return (Dictionary<string, object>)TypeTrees.Read(nodes, ref i, r);
    }

    /// <summary>The file a PPtr's file id points at (0: this one).</summary>
    public GameFile Resolve(int fileId)
    {
        if (fileId == 0) return this;
        if (fileId - 1 >= Externals.Count) return null;
        var name = Externals[fileId - 1];
        if (name.StartsWith("library/", StringComparison.OrdinalIgnoreCase) || name.Contains("unity default resources") || name.Contains("unity_builtin_extra")) return null;
        return Get(name);
    }

    /// <summary>Bytes from a resource stream next to the files (".resS", ".resource").</summary>
    public static byte[] Stream(string path, long offset, int size)
    {
        var file = System.IO.Path.GetFileName(path.Replace("archive:/", "").Replace('\\', '/'));
        var full = System.IO.Path.Combine(UnityEngine.Application.dataPath, file);
        if (!File.Exists(full)) return null;
        using var fs = File.OpenRead(full);
        fs.Position = offset;
        var buf = new byte[size];
        var read = 0;
        while (read < size)
        {
            var n = fs.Read(buf, read, size - read);
            if (n <= 0) break;
            read += n;
        }

        return buf;
    }
}

/// <summary>Little/big-endian reads over a byte array.</summary>
public sealed class Bin
{
    private readonly byte[] _b;
    public int Pos;
    public bool BigEndian;

    public Bin(byte[] b) => _b = b;

    public byte U8() => _b[Pos++];

    public short I16()
    {
        var v = BigEndian ? (short)((_b[Pos] << 8) | _b[Pos + 1]) : BitConverter.ToInt16(_b, Pos);
        Pos += 2;
        return v;
    }

    public ushort U16() => (ushort)I16();

    public int I32()
    {
        int v;
        if (BigEndian) v = (_b[Pos] << 24) | (_b[Pos + 1] << 16) | (_b[Pos + 2] << 8) | _b[Pos + 3];
        else v = BitConverter.ToInt32(_b, Pos);
        Pos += 4;
        return v;
    }

    public uint U32() => (uint)I32();

    public long I64()
    {
        long v;
        if (BigEndian)
        {
            v = 0;
            for (var i = 0; i < 8; i++) v = (v << 8) | _b[Pos + i];
        }
        else v = BitConverter.ToInt64(_b, Pos);

        Pos += 8;
        return v;
    }

    public float F32()
    {
        var v = BitConverter.ToSingle(_b, Pos);
        Pos += 4;
        return v;
    }

    public double F64()
    {
        var v = BitConverter.ToDouble(_b, Pos);
        Pos += 8;
        return v;
    }

    public byte[] Bytes(int n)
    {
        var a = new byte[n];
        Buffer.BlockCopy(_b, Pos, a, 0, n);
        Pos += n;
        return a;
    }

    public string CString()
    {
        var start = Pos;
        while (_b[Pos] != 0) Pos++;
        var s = Encoding.UTF8.GetString(_b, start, Pos - start);
        Pos++;
        return s;
    }

    public void Align(int n) => Pos = (Pos + n - 1) / n * n;
}

/// <summary>The engine's field layouts (UnityTypes.txt) and a generic reader over them.</summary>
public static class TypeTrees
{
    public sealed class Node
    {
        public int Level, Size, Meta;
        public string Type, Name;
        public readonly List<Node> Children = new();
        public bool Aligned => (Meta & 0x4000) != 0;
    }

    private static Dictionary<int, Node> _classes;

    public static Node Of(int classId)
    {
        Load();
        return _classes.TryGetValue(classId, out var n) ? n : null;
    }

    private static void Load()
    {
        if (_classes != null) return;
        _classes = new Dictionary<int, Node>();
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("UnityTypes.txt");
        if (s == null) return;
        using var tr = new StreamReader(s);
        var stack = new List<Node>();
        var cls = -1;
        string line;
        while ((line = tr.ReadLine()) != null)
        {
            if (line.Length == 0) continue;
            if (line.StartsWith("#class"))
            {
                cls = int.Parse(line.Split(' ')[1]);
                stack.Clear();
                continue;
            }

            var f = line.Split('\t');
            var n = new Node { Level = int.Parse(f[0]), Type = f[1], Name = f[2], Size = int.Parse(f[3]), Meta = int.Parse(f[4]) };
            while (stack.Count > n.Level) stack.RemoveAt(stack.Count - 1);
            if (stack.Count > 0) stack[stack.Count - 1].Children.Add(n);
            else _classes[cls] = n;
            stack.Add(n);
        }
    }

    // ReSharper disable once UnusedParameter.Global (kept for symmetry with the recursive calls)
    public static object Read(Node node, ref int unused, Bin r) => Value(node, r);

    private static object Value(Node n, Bin r)
    {
        object v;
        var align = n.Aligned;
        switch (n.Type)
        {
            case "SInt8": v = (sbyte)r.U8(); break;
            case "UInt8" or "char": v = r.U8(); break;
            case "bool": v = r.U8() != 0; break;
            case "SInt16" or "short": v = r.I16(); break;
            case "UInt16" or "unsigned short": v = r.U16(); break;
            case "SInt32" or "int": v = r.I32(); break;
            case "UInt32" or "unsigned int" or "Type*": v = r.U32(); break;
            case "SInt64" or "long long": v = r.I64(); break;
            case "UInt64" or "unsigned long long" or "FileSize": v = (ulong)r.I64(); break;
            case "float": v = r.F32(); break;
            case "double": v = r.F64(); break;
            case "string":
            {
                var len = r.I32();
                v = Encoding.UTF8.GetString(r.Bytes(len));
                if (n.Children.Count > 0 && n.Children[0].Aligned) align = true;
                break;
            }
            case "TypelessData":
            {
                var len = r.I32();
                v = r.Bytes(len);
                break;
            }
            case "pair":
                v = new object[] { Value(n.Children[0], r), Value(n.Children[1], r) };
                break;
            default:
                if (n.Children.Count > 0 && n.Children[0].Type == "Array")
                {
                    var arr = n.Children[0];
                    if (arr.Aligned) align = true;
                    var count = r.I32();
                    var elem = arr.Children[1];
                    if (elem.Type is "UInt8" or "char" or "SInt8") v = r.Bytes(count);
                    else
                    {
                        var list = new List<object>(count);
                        for (var i = 0; i < count; i++) list.Add(Value(elem, r));
                        v = list;
                    }
                }
                else
                {
                    var d = new Dictionary<string, object>();
                    foreach (var c in n.Children) d[c.Name] = Value(c, r);
                    v = d;
                }

                break;
        }

        if (align) r.Align(4);
        return v;
    }
}
