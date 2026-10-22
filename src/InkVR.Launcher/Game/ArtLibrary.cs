using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InkVR.Launcher.Game
{
    /// <summary>
    /// The game's own menu artwork, read from the player's installation (resources.assets) the first time
    /// and cached as PNGs in %LOCALAPPDATA%\InkVR\art. Nothing from the game ships with InkVR.
    /// </summary>
    public static class ArtLibrary
    {
        // name -> optional crop (the 1920x1920 menu textures hold a 1920x1080 picture in the middle)
        private static readonly (string Name, Int32Rect? Crop)[] Wanted =
        {
            ("menu_bg", Screen16x9),
            ("menu_frame", Screen16x9),
            ("menu_lightcookie", Screen16x9),
            ("menu_circle", null),
            ("batim_logo", null),
            ("BendyIcon", null),
            ("bendy_stamp_ui", null),
            ("Bendy Loader - 01 Blank", null),
            ("Bendy Loader - 02", null),
            ("Bendy Loader - 03", null),
            ("Bendy Loader - 04", null),
            ("Bendy Loader - 05 Face", null),
            ("bendy_pose_02", null),
            ("bendy_pose_03", null),
            ("bendy_pose_11", null),
            ("bendy_pose_13", null),
            ("bendy_pose_14", null),
            ("Large Dark Horizontal Splash", null),
            ("Horizontal Splash 01", null),
            ("Options Background", null),
            ("ink_splat_decal", null),
            ("menu_page", null),
            ("loading_image_ch1", Screen16x9),
            ("loading_image_ch2", Screen16x9),
            ("loading_image_ch3", Screen16x9),
            ("loading_image_ch4", Screen16x9),
            ("loading_image_ch5", Screen16x9),
        };

        private static Int32Rect? Screen16x9 => new Int32Rect(0, 420, 1920, 1080);
        private const int CacheVersion = 1;

        public static readonly Dictionary<string, BitmapSource> Images = new Dictionary<string, BitmapSource>(StringComparer.Ordinal);
        public static bool Loaded => Images.Count > 0;

        public static BitmapSource Get(string name) => Images.TryGetValue(name, out var b) ? b : null;

        /// <summary>Loads every image (from the cache, extracting first if needed). Safe to call off the UI thread.</summary>
        public static void Load(GameInstall game)
        {
            if (game == null) return;
            var assets = Path.Combine(game.DataDir, "resources.assets");
            if (!File.Exists(assets)) return;

            var stampFile = Path.Combine(LauncherPaths.ArtCache, "source.txt");
            var stamp = $"{CacheVersion}|{new FileInfo(assets).Length}|{File.GetLastWriteTimeUtc(assets).Ticks}";
            var cacheOk = File.Exists(stampFile) && File.ReadAllText(stampFile) == stamp &&
                          Wanted.All(w => File.Exists(CachePath(w.Name)));

            if (!cacheOk)
            {
                Directory.CreateDirectory(LauncherPaths.ArtCache);
                Extract(assets, (name, bmp) => SavePng(bmp, CachePath(name)));
                File.WriteAllText(stampFile, stamp);
            }

            foreach (var w in Wanted)
            {
                var path = CachePath(w.Name);
                if (!File.Exists(path)) continue;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                lock (Images) Images[w.Name] = bmp;
            }
        }

        /// <summary>Developer aid: writes every image straight from the game files (no cache) to a folder.</summary>
        public static void Export(GameInstall game, string dir)
        {
            if (game == null) throw new InvalidOperationException("Game not found");
            Directory.CreateDirectory(dir);
            Extract(Path.Combine(game.DataDir, "resources.assets"), (name, bmp) => SavePng(bmp, Path.Combine(dir, name + ".png")));
        }

        private static void Extract(string assetsPath, Action<string, BitmapSource> sink)
        {
            var file = new UnityAssetsFile(assetsPath);
            foreach (var w in Wanted)
            {
                if (!file.Textures.TryGetValue(w.Name, out var info)) continue;
                try
                {
                    var bgra = UnityAssetsFile.ReadPixels(info);
                    BitmapSource bmp = BitmapSource.Create(info.Width, info.Height, 96, 96, PixelFormats.Bgra32, null, bgra, info.Width * 4);
                    if (w.Crop is Int32Rect c && c.X + c.Width <= info.Width && c.Y + c.Height <= info.Height)
                        bmp = new CroppedBitmap(bmp, c);
                    bmp.Freeze();
                    sink(w.Name, bmp);
                }
                catch (Exception e)
                {
                    LauncherLog.Write($"Art '{w.Name}' (format {info.Format}): {e.Message}");
                }
            }
        }

        private static string CachePath(string name) => Path.Combine(LauncherPaths.ArtCache, name + ".png");

        private static void SavePng(BitmapSource bmp, string path)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }

    public static class LauncherLog
    {
        private static readonly object Gate = new object();

        public static string FilePath => Path.Combine(LauncherPaths.AppData, "launcher.log");

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(LauncherPaths.AppData);
                    File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
                }
            }
            catch
            {
                // logging must never break the launcher
            }
        }
    }
}
