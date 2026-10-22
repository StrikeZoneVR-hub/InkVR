using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InkVR.Launcher.Game
{
    /// <summary>
    /// Minimal reader for Unity 2022.3 serialized files (format 22, no type trees) — just enough to find
    /// Texture2D objects by name and read their pixels, so the launcher can show the game's own artwork
    /// straight from the player's installation (nothing from the game is redistributed with InkVR).
    /// </summary>
    public sealed class UnityAssetsFile
    {
        public sealed class TextureInfo
        {
            public string Name;
            public int Width, Height, Format, MipCount;
            public long Offset;      // into StreamPath (or inline data in the assets file)
            public int Size;
            public string StreamPath;
            public byte[] Inline;
        }

        private const int ClassTexture2D = 28;
        private readonly string _path;
        public readonly Dictionary<string, TextureInfo> Textures = new Dictionary<string, TextureInfo>(StringComparer.Ordinal);

        public UnityAssetsFile(string path)
        {
            _path = path;
            using (var fs = File.OpenRead(path))
                Parse(fs);
        }

        private void Parse(Stream s)
        {
            var be = new Reader(s, true);
            be.Skip(8);
            var version = be.U32();
            if (version < 22) throw new NotSupportedException($"Serialized file version {version} (expected 22+)");
            be.Skip(4);
            var little = be.U8() == 0;
            be.Skip(3);
            be.U32(); // metadata size
            be.I64(); // file size
            var dataOffset = be.I64();
            be.I64();

            var r = new Reader(s, !little);
            r.CString(); // unity version
            r.I32(); // platform
            var typeTree = r.U8() != 0;
            if (typeTree) throw new NotSupportedException("Type trees are not expected in this build");

            var typeCount = r.I32();
            var classIds = new int[typeCount];
            for (var i = 0; i < typeCount; i++)
            {
                var classId = r.I32();
                r.U8(); // stripped
                var scriptIndex = r.I16();
                if (classId == 114) r.Skip(16); // script id
                r.Skip(16); // old type hash
                classIds[i] = classId;
                _ = scriptIndex;
            }

            var objectCount = r.I32();
            var textures = new List<(long start, uint size)>();
            for (var i = 0; i < objectCount; i++)
            {
                r.Align(4);
                r.I64(); // path id
                var start = r.I64();
                var size = r.U32();
                var typeIndex = r.I32();
                if (typeIndex >= 0 && typeIndex < typeCount && classIds[typeIndex] == ClassTexture2D) textures.Add((start, size));
            }

            foreach (var t in textures)
            {
                s.Position = dataOffset + t.start;
                var info = ReadTexture(new Reader(s, !little));
                if (info != null && !Textures.ContainsKey(info.Name)) Textures[info.Name] = info;
            }
        }

        private TextureInfo ReadTexture(Reader r)
        {
            var t = new TextureInfo { Name = r.AlignedString() };
            r.I32(); // forced fallback format
            r.U8();
            r.U8();
            r.Align(4);
            t.Width = r.I32();
            t.Height = r.I32();
            r.U32(); // complete image size
            r.I32(); // mips stripped
            t.Format = r.I32();
            t.MipCount = r.I32();
            r.U8(); // readable
            r.U8(); // preprocessed
            r.U8(); // ignore mipmap limit
            r.Align(4);
            r.AlignedString(); // mipmap limit group
            r.U8(); // streaming mipmaps
            r.Align(4);
            r.I32(); // streaming priority
            r.I32(); // image count
            r.I32(); // dimension
            r.Skip(24); // texture settings
            r.I32(); // lightmap format
            r.I32(); // colour space
            var blob = r.I32();
            r.Skip(blob);
            r.Align(4);
            var inline = r.I32();
            if (inline > 0) t.Inline = r.Bytes(inline);
            r.Align(4);
            t.Offset = (long)r.U64();
            t.Size = (int)r.U32();
            var stream = r.AlignedString();
            if (!string.IsNullOrEmpty(stream)) t.StreamPath = Path.Combine(Path.GetDirectoryName(_path) ?? "", stream);
            if (t.Width <= 0 || t.Height <= 0 || t.Width > 16384 || t.Height > 16384) return null;
            return t;
        }

        /// <summary>Top mip level as BGRA32, rows top-to-bottom (ready for WPF).</summary>
        public static byte[] ReadPixels(TextureInfo t)
        {
            byte[] data;
            if (t.Inline != null && t.Inline.Length > 0)
            {
                data = t.Inline;
            }
            else
            {
                data = new byte[t.Size];
                using (var fs = File.OpenRead(t.StreamPath))
                {
                    fs.Position = t.Offset;
                    var read = 0;
                    while (read < data.Length)
                    {
                        var n = fs.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }
            }

            return TextureDecoder.Decode(data, t.Width, t.Height, t.Format);
        }

        private sealed class Reader
        {
            private readonly Stream _s;
            private readonly bool _big;
            private readonly byte[] _buf = new byte[8];

            public Reader(Stream s, bool bigEndian)
            {
                _s = s;
                _big = bigEndian;
            }

            private byte[] Read(int n)
            {
                var got = _s.Read(_buf, 0, n);
                if (got != n) throw new EndOfStreamException();
                if (_big && n > 1) Array.Reverse(_buf, 0, n);
                return _buf;
            }

            public byte U8() => Read(1)[0];
            public short I16() => BitConverter.ToInt16(Read(2), 0);
            public int I32() => BitConverter.ToInt32(Read(4), 0);
            public uint U32() => BitConverter.ToUInt32(Read(4), 0);
            public long I64() => BitConverter.ToInt64(Read(8), 0);
            public ulong U64() => BitConverter.ToUInt64(Read(8), 0);
            public void Skip(int n) => _s.Position += n;

            public byte[] Bytes(int n)
            {
                var b = new byte[n];
                var got = 0;
                while (got < n)
                {
                    var k = _s.Read(b, got, n - got);
                    if (k <= 0) throw new EndOfStreamException();
                    got += k;
                }

                return b;
            }

            public void Align(int a)
            {
                var p = _s.Position;
                var m = p % a;
                if (m != 0) _s.Position = p + (a - m);
            }

            public string CString()
            {
                var sb = new StringBuilder();
                int c;
                while ((c = _s.ReadByte()) > 0) sb.Append((char)c);
                return sb.ToString();
            }

            public string AlignedString()
            {
                var len = I32();
                if (len < 0 || len > 4096) throw new InvalidDataException("bad string length");
                var s = Encoding.UTF8.GetString(Bytes(len));
                Align(4);
                return s;
            }
        }
    }
}
