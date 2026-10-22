using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace InkVR.Launcher.Game
{
    /// <summary>
    /// Installs InkVR into the game folder: BepInEx + the InkVR plugin from the release's InkVR-Payload folder,
    /// and Unity's OpenXR provider downloaded from Unity's package registry (never redistributed by InkVR).
    /// </summary>
    public static class ModInstaller
    {
        public const string OpenXRPackageUrl = "https://download.packages.unity.com/com.unity.xr.openxr/-/com.unity.xr.openxr-1.17.1.tgz";
        public const string OpenXRPackageSha256 = "e8020f098af243204688c31ed87927e29ea703020928930fe22ddcd2dc704240";

        public static bool BepInExInstalled(GameInstall g) =>
            File.Exists(Path.Combine(g.Dir, "winhttp.dll")) &&
            File.Exists(Path.Combine(g.Dir, "doorstop_config.ini")) &&
            File.Exists(Path.Combine(g.BepInExDir, "core", "BepInEx.Unity.IL2CPP.dll"));

        public static bool InteropGenerated(GameInstall g) => File.Exists(Path.Combine(g.BepInExDir, "interop", "Assembly-CSharp.dll"));

        public static bool ProviderInstalled(GameInstall g) =>
            File.Exists(g.OpenXRProvider) && File.Exists(g.OpenXRLoader) && File.Exists(g.SubsystemManifest);

        public static Version InstalledVersion(GameInstall g) => DllVersion(g.PluginDll);

        public static string PayloadDir => Directory.Exists(LauncherPaths.Payload) ? LauncherPaths.Payload : null;

        public static Version PayloadVersion =>
            PayloadDir == null ? null : DllVersion(Path.Combine(PayloadDir, "BepInEx", "plugins", "InkVR", "InkVR.dll"));

        public static bool NeedsInstall(GameInstall g) =>
            !BepInExInstalled(g) || !ProviderInstalled(g) || InstalledVersion(g) == null ||
            (PayloadVersion != null && PayloadVersion > InstalledVersion(g));

        private static Version DllVersion(string path)
        {
            if (!File.Exists(path)) return null;
            var v = FileVersionInfo.GetVersionInfo(path);
            return new Version(v.FileMajorPart, v.FileMinorPart, v.FileBuildPart);
        }

        public static void Install(GameInstall g, Action<string> status)
        {
            if (PayloadDir != null)
            {
                status("Copying BepInEx and InkVR...");
                CopyTree(PayloadDir, g.Dir);
            }
            else if (!BepInExInstalled(g) || InstalledVersion(g) == null)
            {
                throw new InvalidOperationException("The InkVR-Payload folder is missing. Keep it next to InkVR Launcher.exe (it comes in the release zip).");
            }

            if (!ProviderInstalled(g)) InstallProvider(g, status);
            status("InkVR is installed.");
            LauncherLog.Write("Install complete: plugin " + InstalledVersion(g));
        }

        private static void CopyTree(string from, string to)
        {
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('\\')));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(from.Length).TrimStart('\\');
                var dest = Path.Combine(to, rel);
                // Keep the player's own settings.
                if (rel.StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase) && File.Exists(dest)) continue;
                File.Copy(file, dest, true);
            }
        }

        private static void InstallProvider(GameInstall g, Action<string> status)
        {
            status("Downloading Unity's OpenXR plugin (27 MB)...");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            byte[] tgz;
            using (var web = new WebClient())
            {
                web.Headers[HttpRequestHeader.UserAgent] = "InkVR-Launcher";
                tgz = web.DownloadData(OpenXRPackageUrl);
            }

            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(tgz)).Replace("-", "").ToLowerInvariant();
                if (hash != OpenXRPackageSha256) throw new InvalidDataException("The OpenXR package download did not match the expected checksum.");
            }

            status("Installing the OpenXR plugin...");
            Directory.CreateDirectory(Path.GetDirectoryName(g.OpenXRProvider));
            Directory.CreateDirectory(Path.GetDirectoryName(g.SubsystemManifest));
            var wanted = new (string Entry, string Dest)[]
            {
                ("package/Runtime/windows/x64/UnityOpenXR.dll", g.OpenXRProvider),
                ("package/RuntimeLoaders/windows/x64/openxr_loader.dll", g.OpenXRLoader),
                ("package/Runtime/UnitySubsystemsManifest.json", g.SubsystemManifest),
            };
            using (var gz = new GZipStream(new MemoryStream(tgz), CompressionMode.Decompress))
            {
                foreach (var (name, data) in Tar(gz))
                foreach (var w in wanted)
                    if (name == w.Entry)
                        File.WriteAllBytes(w.Dest, data);
            }

            if (!ProviderInstalled(g)) throw new InvalidDataException("The OpenXR package did not contain the expected files.");
        }

        private static System.Collections.Generic.IEnumerable<(string Name, byte[] Data)> Tar(Stream s)
        {
            var header = new byte[512];
            string paxPath = null;
            while (ReadFull(s, header, 512))
            {
                if (header[0] == 0) yield break;
                var name = Cstr(header, 0, 100);
                var prefix = Cstr(header, 345, 155);
                if (prefix.Length > 0) name = prefix + "/" + name;
                var size = Convert.ToInt64(Cstr(header, 124, 12).Trim().Trim('\0').PadLeft(1, '0'), 8);
                var type = (char)header[156];
                var data = new byte[size];
                ReadFull(s, data, (int)size);
                var pad = (int)((512 - size % 512) % 512);
                if (pad > 0) ReadFull(s, new byte[pad], pad);

                if (type == 'x')
                {
                    foreach (var rec in Encoding.UTF8.GetString(data).Split('\n'))
                    {
                        var i = rec.IndexOf(" path=", StringComparison.Ordinal);
                        if (i >= 0) paxPath = rec.Substring(i + 6);
                    }

                    continue;
                }

                if (paxPath != null)
                {
                    name = paxPath;
                    paxPath = null;
                }

                if (type == '0' || type == '\0') yield return (name, data);
            }
        }

        private static string Cstr(byte[] b, int off, int len)
        {
            var end = off;
            while (end < off + len && b[end] != 0) end++;
            return Encoding.ASCII.GetString(b, off, end - off);
        }

        private static bool ReadFull(Stream s, byte[] buf, int n)
        {
            var got = 0;
            while (got < n)
            {
                var k = s.Read(buf, got, n - got);
                if (k <= 0) return false;
                got += k;
            }

            return true;
        }

        /// <summary>Turns the mod off by removing BepInEx's loader and InkVR's files (game files are untouched).</summary>
        public static void Uninstall(GameInstall g)
        {
            foreach (var f in new[]
                     {
                         Path.Combine(g.Dir, "winhttp.dll"), Path.Combine(g.Dir, "doorstop_config.ini"), Path.Combine(g.Dir, ".doorstop_version"),
                         g.OpenXRProvider, g.OpenXRLoader, g.SubsystemManifest,
                     })
                if (File.Exists(f))
                    File.Delete(f);
            var plugin = Path.GetDirectoryName(g.PluginDll);
            if (plugin != null && Directory.Exists(plugin)) Directory.Delete(plugin, true);
            LauncherLog.Write("Uninstalled InkVR from " + g.Dir);
        }
    }
}
