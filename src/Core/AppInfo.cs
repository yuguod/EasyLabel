using System;
using System.Reflection;

namespace EasyLabel.Core
{
    /// <summary>
    /// 产品标识集中在这里：改名称、版权行、标语只需要动本文件，
    /// 界面标题、关于页、打印作业名、ZPL 注释都从这里取值。
    /// </summary>
    public static class AppInfo
    {
        public const string ProductName = "EasyLabel 标签轻松打";
        public const string ShortName = "EasyLabel";
        public const string Tagline = "标签打印 · ZPL 直发 · 203 / 300 / 600 dpi · 40×15 / 30×25 / 30×20 / 自定义";

        /// <summary>著作权人。署名用账号名，若要强化权利主张可改为真实姓名。</summary>
        public const string Author = "yuguod";
        public const string ProjectUrl = "https://github.com/yuguod/EasyLabel";
        public const string Copyright = "Copyright © 2026 " + Author + ". 保留所有权利。";
        public const string ReleaseDate = "2026 年 10 月";

        private static string _version;

        /// <summary>形如 1.5.1；取入口程序集版本，取不到时退回 1.0.0。</summary>
        public static string Version
        {
            get
            {
                if (_version != null) return _version;
                try
                {
                    Assembly asm = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
                    System.Version v = asm.GetName().Version;
                    _version = v == null ? "1.0.0" : v.ToString(3);
                }
                catch
                {
                    _version = "1.0.0";
                }
                return _version;
            }
        }
    }
}
