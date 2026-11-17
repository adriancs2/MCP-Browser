using System;
using System.Drawing;
using System.Reflection;

namespace MCP_Browser.Browser
{
    /// <summary>Product identity shared by the window, the About dialog and the icon.</summary>
    internal static class AppInfo
    {
        public const string WindowTitle = "MCP Browser for AI";
        public const string ProductName = "MCP Browser";
        public const string Tagline = "Open source freeware";
        public const string ProjectUrl = "https://github.com/adriancs2/MCP-Browser";

        private static Icon _icon;

        /// <summary>The multi-resolution application icon embedded in the assembly.</summary>
        public static Icon Icon
        {
            get
            {
                if (_icon == null)
                {
                    try
                    {
                        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                        {
                            if (s != null) _icon = new Icon(s);
                        }
                    }
                    catch { }
                    if (_icon == null) _icon = SystemIcons.Application;
                }
                return _icon;
            }
        }

        public static string Version
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return $"{v.Major}.{v.Minor}.{v.Build}";
            }
        }
    }
}
