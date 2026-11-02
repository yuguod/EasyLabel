using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace EasyLabel.Core
{
    /// <summary>用户导入的一个字体文件。</summary>
    public sealed class CustomFontInfo
    {
        public string FileName { get; set; }
        public string FamilyName { get; set; }
        public string FilePath { get; set; }
        public long SizeBytes { get; set; }

        /// <summary>优先显示字体的真实族名，读不到时退回文件名。</summary>
        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(FamilyName)) return FamilyName;
                return Path.GetFileNameWithoutExtension(FileName ?? "");
            }
        }

        public string SizeText
        {
            get
            {
                if (SizeBytes < 1024) return SizeBytes + " B";
                if (SizeBytes < 1024 * 1024) return (SizeBytes / 1024.0).ToString("0.0") + " KB";
                return (SizeBytes / (1024.0 * 1024.0)).ToString("0.0") + " MB";
            }
        }
    }

    /// <summary>
    /// 程序私有的字体库。
    ///
    /// 设计要点：
    /// · 字体文件不装进系统，也不调用 AddFontResourceEx——那样会锁住文件、卸载要重启；
    ///   这里用 PrivateFontCollection.AddMemoryFont 从内存加载，文件随时可删。
    /// · 加载后的内存块不释放（GDI+ 可能在进程生命周期内继续引用），一个字体几百 KB，可接受。
    /// · 私有字体与系统字体同名时优先用私有的，方便用户覆盖某个字体的显示效果。
    /// </summary>
    public static class FontLibrary
    {
        public const string DefaultFamily = "Microsoft YaHei UI";

        private static readonly object Sync = new object();
        private static readonly List<CustomFontInfo> Custom = new List<CustomFontInfo>();
        // 保存已分配的非托管内存，避免 GDI+ 继续引用时悬垂。
        private static readonly List<IntPtr> PinnedMemory = new List<IntPtr>();
        private static PrivateFontCollection _collection;
        private static List<string> _familyCache;
        private static string _overrideDirectory;
        private static bool _loaded;

        /// <summary>字体目录。可写，测试时指向临时目录以免污染用户数据。</summary>
        public static string FontDirectory
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_overrideDirectory)) return _overrideDirectory;
                return Path.Combine(AppSettings.DataDirectory, "Fonts");
            }
            set { _overrideDirectory = value; }
        }

        /// <summary>支持的扩展名。ttc 是字体集合，一个文件里可能有多套字体。</summary>
        public static readonly string[] Extensions = { ".ttf", ".otf", ".ttc" };

        public static event EventHandler Changed;

        public static bool IsFontFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext)) return false;
            foreach (string allowed in Extensions)
            {
                if (string.Equals(ext, allowed, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public static string BuildFilter()
        {
            return "字体文件 (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|TrueType 字体 (*.ttf)|*.ttf|OpenType 字体 (*.otf)|*.otf|所有文件 (*.*)|*.*";
        }

        public static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (Sync)
            {
                if (_loaded) return;
                _loaded = true;
                Reload();
            }
        }

        /// <summary>重新扫描字体目录。旧的 collection 故意不 Dispose：可能还有 Font 对象引用它。</summary>
        public static void Reload()
        {
            lock (Sync)
            {
                var collection = new PrivateFontCollection();
                var items = new List<CustomFontInfo>();
                string dir = FontDirectory;

                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                catch (Exception ex)
                {
                    Logger.Info("字体目录不可用：" + dir + "（" + ex.Message + "）");
                }

                if (Directory.Exists(dir))
                {
                    string[] files;
                    try { files = Directory.GetFiles(dir); }
                    catch (Exception ex)
                    {
                        Logger.Info("读取字体目录失败：" + ex.Message);
                        files = new string[0];
                    }

                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in files)
                    {
                        if (!IsFontFile(file)) continue;
                        string family = TryLoad(collection, file);
                        var info = new CustomFontInfo
                        {
                            FileName = Path.GetFileName(file),
                            FilePath = file,
                            FamilyName = family,
                            SizeBytes = SafeLength(file)
                        };
                        items.Add(info);
                        if (string.IsNullOrEmpty(family))
                            Logger.Info("字体加载失败（文件可能已损坏）：" + file);
                    }
                }

                _collection = collection;
                _familyCache = null;
                Custom.Clear();
                Custom.AddRange(items);
            }
        }

        private static long SafeLength(string file)
        {
            try { return new FileInfo(file).Length; }
            catch { return 0; }
        }

        /// <summary>把文件读进内存再加到 collection，返回新增的字体族名；失败返回 null。</summary>
        private static string TryLoad(PrivateFontCollection collection, string file)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(file); }
            catch (Exception ex)
            {
                Logger.Info("读取字体文件失败：" + file + "（" + ex.Message + "）");
                return null;
            }
            return TryLoadBytes(collection, bytes);
        }

        private static string TryLoadBytes(PrivateFontCollection collection, byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12) return null;
            IntPtr memory = IntPtr.Zero;
            try
            {
                memory = Marshal.AllocHGlobal(bytes.Length);
                Marshal.Copy(bytes, 0, memory, bytes.Length);
                int before = collection.Families.Length;
                collection.AddMemoryFont(memory, bytes.Length);
                FontFamily[] families = collection.Families;
                if (families.Length > before)
                {
                    // 内存交给进程持有，不复用这块地址，避免 collection 内部仍引用。
                    PinnedMemory.Add(memory);
                    return families[families.Length - 1].Name;
                }
                Marshal.FreeHGlobal(memory);
                return null;
            }
            catch (Exception ex)
            {
                if (memory != IntPtr.Zero)
                {
                    try { Marshal.FreeHGlobal(memory); } catch { }
                }
                Logger.Info("加载字体失败：" + ex.Message);
                return null;
            }
        }

        public static IList<CustomFontInfo> GetCustomFonts()
        {
            EnsureLoaded();
            lock (Sync)
            {
                return new List<CustomFontInfo>(Custom);
            }
        }

        /// <summary>系统已安装字体 + 私有字体，去重后按名称排序。</summary>
        public static IList<string> GetFamilies()
        {
            EnsureLoaded();
            lock (Sync)
            {
                if (_familyCache != null) return new List<string>(_familyCache);

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var list = new List<string>();
                try
                {
                    using (var installed = new InstalledFontCollection())
                    {
                        foreach (FontFamily family in installed.Families)
                        {
                            if (family == null) continue;
                            string name = family.Name;
                            if (string.IsNullOrWhiteSpace(name)) continue;
                            if (seen.Add(name)) list.Add(name);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Info("枚举系统字体失败：" + ex.Message);
                }

                try
                {
                    foreach (FontFamily family in _collection.Families)
                    {
                        if (family == null) continue;
                        string name = family.Name;
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (seen.Add(name)) list.Add(name);
                    }
                }
                catch { }

                if (list.Count == 0) list.Add(DefaultFamily);
                list.Sort(StringComparer.CurrentCultureIgnoreCase);
                _familyCache = list;
                return new List<string>(list);
            }
        }

        /// <summary>常用字体排在最前面，其余按字母序。</summary>
        public static IList<string> GetFamiliesForPicker()
        {
            IList<string> all = GetFamilies();
            var ordered = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] common = { DefaultFamily, "Microsoft YaHei", "SimHei", "SimSun", "Segoe UI", "Arial" };
            foreach (string name in common)
            {
                if (used.Add(name)) ordered.Add(name);
            }
            foreach (string name in all)
            {
                if (used.Add(name)) ordered.Add(name);
            }
            return ordered;
        }

        public static bool IsCustomFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family)) return false;
            return FindCustomFamily(family) != null;
        }

        private static FontFamily FindCustomFamily(string family)
        {
            if (string.IsNullOrWhiteSpace(family) || _collection == null) return null;
            try
            {
                foreach (FontFamily candidate in _collection.Families)
                {
                    if (candidate != null && string.Equals(candidate.Name, family, StringComparison.OrdinalIgnoreCase))
                        return candidate;
                }
            }
            catch { }
            return null;
        }

        /// <summary>统一的字体创建入口：私有字体优先，其次系统字体，最后退回无衬线。</summary>
        public static Font CreateFont(string family, float px, bool bold)
        {
            EnsureLoaded();
            if (px < 1f) px = 1f;
            FontStyle style = bold ? FontStyle.Bold : FontStyle.Regular;

            FontFamily custom = FindCustomFamily(family);
            if (custom != null)
            {
                try { return new Font(custom, px, style, GraphicsUnit.Pixel); }
                catch
                {
                    try { return new Font(custom, px, FontStyle.Regular, GraphicsUnit.Pixel); }
                    catch { }
                }
            }

            string name = string.IsNullOrWhiteSpace(family) ? DefaultFamily : family;
            try { return new Font(name, px, style, GraphicsUnit.Pixel); }
            catch
            {
                try { return new Font(name, px, FontStyle.Regular, GraphicsUnit.Pixel); }
                catch
                {
                    return new Font(FontFamily.GenericSansSerif, px, FontStyle.Regular, GraphicsUnit.Pixel);
                }
            }
        }

        /// <summary>导入字体文件。返回导入后的信息；文件无效时抛 InvalidDataException。</summary>
        public static CustomFontInfo Import(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("找不到字体文件。", sourcePath);
            if (!IsFontFile(sourcePath))
                throw new InvalidDataException("只支持 .ttf / .otf / .ttc 字体文件。");

            EnsureLoaded();
            string dir = FontDirectory;
            Directory.CreateDirectory(dir);

            string target = Path.Combine(dir, Path.GetFileName(sourcePath));
            target = UniquePath(target);
            File.Copy(sourcePath, target, false);

            // 先在独立 collection 里验证，确认是有效字体再计入列表。
            var probe = new PrivateFontCollection();
            string family = TryLoadBytes(probe, File.ReadAllBytes(target));
            if (string.IsNullOrEmpty(family))
            {
                try { File.Delete(target); } catch { }
                throw new InvalidDataException("这个文件不是有效的字体，或者字体已损坏。");
            }

            lock (Sync)
            {
                if (_collection == null) _collection = new PrivateFontCollection();
                string added = TryLoad(_collection, target);
                var info = new CustomFontInfo
                {
                    FileName = Path.GetFileName(target),
                    FilePath = target,
                    FamilyName = string.IsNullOrEmpty(added) ? family : added,
                    SizeBytes = SafeLength(target)
                };
                Custom.Add(info);
                _familyCache = null;
                Logger.Info("已导入字体：" + info.FileName + " → " + info.DisplayName);
                RaiseChanged();
                return info;
            }
        }

        private static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; i < 1000; i++)
            {
                string candidate = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(candidate)) return candidate;
            }
            return Path.Combine(dir, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ext);
        }

        /// <summary>删除字体文件。当前进程里已加载的字形会保留到程序退出。</summary>
        public static bool Remove(CustomFontInfo font)
        {
            if (font == null) return false;
            EnsureLoaded();
            bool deleted = false;
            try
            {
                if (!string.IsNullOrEmpty(font.FilePath) && File.Exists(font.FilePath))
                {
                    File.Delete(font.FilePath);
                    deleted = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Info("删除字体失败：" + font.FilePath + "（" + ex.Message + "）");
                return false;
            }

            lock (Sync)
            {
                Custom.RemoveAll(f => string.Equals(f.FilePath, font.FilePath, StringComparison.OrdinalIgnoreCase));
                _familyCache = null;
            }
            Logger.Info("已删除字体：" + font.FileName);
            RaiseChanged();
            return deleted;
        }

        private static void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;
            try { handler(null, EventArgs.Empty); }
            catch (Exception ex) { Logger.Info("字体变更通知失败：" + ex.Message); }
        }

        /// <summary>测试用：清空缓存并释放目录覆盖。</summary>
        public static void ResetForTests()
        {
            lock (Sync)
            {
                Custom.Clear();
                _collection = null;
                _familyCache = null;
                _loaded = false;
                _overrideDirectory = null;
            }
        }
    }
}
