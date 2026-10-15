using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace Keyward.Ui.Design
{
    /// <summary>
    /// The Keyward icon set, drawn as vector geometry in code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These glyphs are original, drawn on a 24x24 grid with a 1.8 unit stroke, round caps and
    /// round joins, so every icon shares the same optical weight. Drawing them as geometry rather
    /// than shipping an icon font keeps the application dependency-free, avoids any third-party
    /// licensing question, and lets the icons recolour with the theme at runtime.
    /// </para>
    /// <para>
    /// Everything is built from lines and cubic segments only - no elliptical arc commands - so the
    /// geometry parses identically on every Windows build rather than depending on the renderer's
    /// arc handling.
    /// </para>
    /// </remarks>
    public static class Icons
    {
        private static readonly Dictionary<string, Geometry> Cache =
            new Dictionary<string, Geometry>(StringComparer.Ordinal);

        private static string N(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>A closed circle built from four cubic segments.</summary>
        public static string Circle(double cx, double cy, double r)
        {
            const double K = 0.5522847498307933;
            double k = r * K;

            StringBuilderLike sb = new StringBuilderLike();
            sb.Add("M ", cx, ",", cy - r);
            sb.Add(" C ", cx, ",", cy - r + k, " ", cx + r - k, ",", cy, " ", cx + r, ",", cy);
            sb.Add(" C ", cx + r, ",", cy + k, " ", cx + k, ",", cy + r, " ", cx, ",", cy + r);
            sb.Add(" C ", cx - k, ",", cy + r, " ", cx - r, ",", cy + k, " ", cx - r, ",", cy);
            sb.Add(" C ", cx - r, ",", cy - k, " ", cx - k, ",", cy - r, " ", cx, ",", cy - r);
            sb.Add(" Z");
            return sb.ToString();
        }

        /// <summary>A closed rounded rectangle.</summary>
        public static string RoundRect(double x, double y, double w, double h, double r)
        {
            StringBuilderLike sb = new StringBuilderLike();
            sb.Add("M ", x + r, ",", y);
            sb.Add(" L ", x + w - r, ",", y);
            sb.Add(" C ", x + w, ",", y, " ", x + w, ",", y, " ", x + w, ",", y + r);
            sb.Add(" L ", x + w, ",", y + h - r);
            sb.Add(" C ", x + w, ",", y + h, " ", x + w, ",", y + h, " ", x + w - r, ",", y + h);
            sb.Add(" L ", x + r, ",", y + h);
            sb.Add(" C ", x, ",", y + h, " ", x, ",", y + h, " ", x, ",", y + h - r);
            sb.Add(" L ", x, ",", y + r);
            sb.Add(" C ", x, ",", y, " ", x, ",", y, " ", x + r, ",", y);
            sb.Add(" Z");
            return sb.ToString();
        }

        private static readonly string VaultBody = RoundRect(3, 3.5, 18, 17, 2.5);
        private const string VaultShackle = "M8,3.5 L8,2.6 C8,0.7 9.8,0.7 12,0.7 C14.2,0.7 16,0.7 16,2.6 L16,3.5";
        private static readonly string VaultDial = Circle(12, 12, 4.2);

        private const string SearchPath = "M10.5,3.4 C6.6,3.4 3.5,6.5 3.5,10.4 C3.5,14.3 6.6,17.4 10.5,17.4 C14.4,17.4 17.5,14.3 17.5,10.4 C17.5,6.5 14.4,3.4 10.5,3.4 Z M16.1,16.1 L20.8,20.8";
        private const string PlusPath = "M12,5 L12,19 M5,12 L19,12";
        private const string MinusPath = "M5,12 L19,12";

        private const string StarPath = "M12,3.2 L14.6,9.3 L21.1,10.1 L16.1,14.6 L17.4,21 L12,17.7 L6.6,21 L7.9,14.6 L2.9,10.1 L9.4,9.3 Z";

        private const string NotePath = "M5,3.2 L13.8,3.2 L19,8.4 L19,20.8 L5,20.8 Z";
        private const string NoteLines = "M13.8,3.2 L13.8,8.4 L19,8.4 M8,12.6 L16,12.6 M8,16.4 L13.5,16.4";

        private const string KeyPath = "M15.4,3.6 C12.2,3.6 9.6,6.2 9.6,9.4 C9.6,11.2 10.5,12.8 11.9,13.7 L6.6,19 L6.6,21.4 L9,21.4 L9,19.8 L10.6,19.8 L10.6,18.2 L12.4,18.2 L12.4,16.4 L13.9,15.9 C15.3,14.9 15.9,13.2 15.9,11.5 C15.9,7.2 18.6,3.6 15.4,3.6 Z";
        private const string KeyTeeth = "M16.6,5.4 L20.4,5.4 M18.4,5.4 L18.4,7.6";

        private const string FolderPath = "M3,6.4 L3,19.4 C3,20 3.5,20.5 4.1,20.5 L19.9,20.5 C20.5,20.5 21,20 21,19.4 L21,8.4 C21,7.9 20.5,7.4 20,7.4 L11.4,7.4 L9.4,4.9 L4.1,4.9 C3.5,4.9 3,5.4 3,6.4 Z";

        private const string GearPath =
            "M10.4,2.4 L13.6,2.4 L14.3,5.2 C15,5.4 15.6,5.7 16.2,6.1 L18.9,5.3 L20.8,7.9 " +
            "L18.7,9.9 C18.9,10.6 19,11.3 19,12 C19,12.7 18.9,13.4 18.7,14.1 L20.8,16.1 " +
            "L18.9,18.7 L16.2,17.9 C15.6,18.3 15,18.6 14.3,18.8 L13.6,21.6 L10.4,21.6 " +
            "L9.7,18.8 C9,18.6 8.4,18.3 7.8,17.9 L5.1,18.7 L3.2,16.1 L5.3,14.1 " +
            "C5.1,13.4 5,12.7 5,12 C5,11.3 5.1,10.6 5.3,9.9 L3.2,7.9 L5.1,5.3 " +
            "L7.8,6.1 C8.4,5.7 9,5.4 9.7,5.2 Z";
        private static readonly string GearHole = Circle(12, 12, 3.2);

        private static readonly string CopyBack = RoundRect(3, 3, 12, 14, 2);
        private const string CopyFront = "M8.5,8 L19,8 C19.6,8 20,8.4 20,9 L20,19.5 C20,20.1 19.6,20.5 19,20.5 L8.5,20.5 C7.9,20.5 7.5,20.1 7.5,19.5 L7.5,9 C7.5,8.4 7.9,8 8.5,8 Z M7.5,14 L3.5,14";

        private const string EyePath = "M2.5,12 C5,7 8.4,4.6 12,4.6 C15.6,4.6 19,7 21.5,12 C19,17 15.6,19.4 12,19.4 C8.4,19.4 5,17 2.5,12 Z";
        private const string EyeSlashPath = "M4,4 L20,20 M6.6,8 C5.2,9.2 3.7,10.5 2.5,12 C5,17 8.4,19.4 12,19.4 C14.2,19.4 16.3,18.5 18.1,17 M9.6,5.1 C10.4,4.8 11.2,4.6 12,4.6 C15.6,4.6 19,7 21.5,12 C20.6,13.1 19.6,14.1 18.4,15";
        private static readonly string EyeSlashCircle = Circle(12, 12, 3);

        private const string RefreshPath = "M20.4,12 C20.4,16.7 16.7,20.4 12,20.4 C7.3,20.4 3.6,16.7 3.6,12 C3.6,7.3 7.3,3.6 12,3.6 C15.6,3.6 18.7,5.9 20,9.2 M20.4,3.6 L20.4,9.6 L14.4,9.6";

        private const string TrashPath =
            "M4,6.5 L20,6.5 M9.5,6.5 L9.5,4.6 C9.5,4.1 9.9,3.7 10.4,3.7 L13.6,3.7 C14.1,3.7 14.5,4.1 14.5,4.6 L14.5,6.5 " +
            "M6,6.5 L7,19.9 C7,20.6 7.5,21.1 8.2,21.1 L15.8,21.1 C16.5,21.1 17,20.6 17,19.9 L18,6.5 M10,10 L10,17.5 M14,10 L14,17.5";

        private const string ShieldPath = "M12,3 L20,6 L20,11.5 C20,16.5 16.4,20.2 12,21.5 C7.6,20.2 4,16.5 4,11.5 L4,6 Z";
        private const string WarningPath = "M12,3.5 L22,20.5 L2,20.5 Z";
        private const string WarningBang = "M12,9.5 L12,14.6 M12,17.2 L12,17.5";

        private const string CheckPath = "M4.5,12.6 L9.6,17.6 L19.5,6.6";

        private const string ChevronRightPath = "M9.2,5 L16,12 L9.2,19";
        private const string ChevronLeftPath = "M14.8,5 L8,12 L14.8,19";
        private const string ChevronDownPath = "M5,9.2 L12,16 L19,9.2";

        private const string ArrowUpPath = "M12,20 L12,5 M6,11 L12,5 L18,11";
        private const string ArrowDownPath = "M12,4 L12,19 M6,13 L12,19 L18,13";
        private const string ArrowLeftPath = "M20,12 L5,12 M11,6 L5,12 L11,18";
        private const string ArrowRightPath = "M4,12 L19,12 M13,6 L19,12 L13,18";

        private const string MoonPath = "M20.2,14.6 C19,15.4 17.6,15.9 16.1,15.9 C10.6,15.9 6.1,11.4 6.1,5.9 C6.1,4.4 6.6,3 7.4,1.8 C3.9,3.6 1.5,7.3 1.5,11.6 C1.5,17.7 6.5,22.7 12.6,22.7 C16.9,22.7 20.6,20.3 22.4,16.8 C21.9,16.4 21.1,15.9 20.2,14.6 Z";

        private const string TagPath = "M3.5,11.4 L11.4,3.5 L20.5,3.5 L20.5,12.6 L12.6,20.5 Z M3.5,11.4 L12.6,20.5";
        private static readonly string TagDot = Circle(16.6, 7.4, 1.4);

        private const string ClockHands = "M12,7.2 L12,12.4 L15.8,14.4";

        private const string DownloadArrow = "M12,3.5 L12,15 M7,10.4 L12,15.4 L17,10.4";
        private const string DownloadTray = "M4,16 L4,19.8 L20,19.8 L20,16";
        private const string UploadArrow = "M12,15.4 L12,4 M7,8.6 L12,3.6 L17,8.6";

        private const string ClosePath = "M6,6 L18,18 M18,6 L6,18";

        private static readonly string LockBody = RoundRect(4, 10.4, 16, 10.2, 2);
        private const string LockShackle = "M7.8,10.4 L7.8,7.6 C7.8,5.6 9.7,4 12,4 C14.3,4 16.2,5.6 16.2,7.6 L16.2,10.4";
        private const string UnlockShackle = "M7.8,10.4 L7.8,7.6 C7.8,5.6 9.7,4 12,4 C13.1,4 14.1,4.3 15,4.9";

        private const string LinkLeft = "M9.8,14.2 L14.2,9.8 M10.8,7.2 L12.6,5.4 C14.3,3.7 17.1,3.7 18.8,5.4 C20.5,7.1 20.5,9.9 18.8,11.6 L17,13.4 M13.2,16.8 L11.4,18.6 C9.7,20.3 6.9,20.3 5.2,18.6 C3.5,16.9 3.5,14.1 5.2,12.4 L7,10.6";

        private const string GlobeMeridians = "M3.5,12 L20.5,12 M12,3.5 C9.2,7.3 9.2,16.7 12,20.5 M12,3.5 C14.8,7.3 14.8,16.7 12,20.5";

        private const string UserBody = "M4.5,20.6 C4.5,16.8 7.8,14.2 12,14.2 C16.2,14.2 19.5,16.8 19.5,20.6";
        private static readonly string UserHead = Circle(12, 8, 3.9);

        private const string DbTop = "M3.5,6.4 C3.5,4.7 7.4,3.4 12,3.4 C16.6,3.4 20.5,4.7 20.5,6.4 C20.5,8.1 16.6,9.4 12,9.4 C7.4,9.4 3.5,8.1 3.5,6.4 Z";
        private const string DbBody = "M3.5,6.4 L3.5,17.6 C3.5,19.3 7.4,20.6 12,20.6 C16.6,20.6 20.5,19.3 20.5,17.6 L20.5,6.4";
        private const string DbLines = "M3.5,12 C3.5,13.7 7.4,15 12,15 C16.6,15 20.5,13.7 20.5,12";

        private static readonly string InfoDot = Circle(12, 7.6, 1.15);
        private const string InfoBar = "M12,11.2 L12,17";

        private const string WandStick = "M4.5,19.5 L15.2,8.8 M13.4,10.6 L15.2,8.8 L17.4,11 L15.6,12.8 Z";
        private const string WandStar = "M18.4,2.6 L19.5,5.2 L22.1,6.3 L19.5,7.4 L18.4,10 L17.3,7.4 L14.7,6.3 L17.3,5.2 Z";
        private const string WandSpark = "M7.6,3 L8.4,4.8 L10.2,5.6 L8.4,6.4 L7.6,8.2 L6.8,6.4 L5,5.6 L6.8,4.8 Z";

        private const string GridPath =
            "M4,4 L9.2,4 L9.2,9.2 L4,9.2 Z M14.8,4 L20,4 L20,9.2 L14.8,9.2 Z " +
            "M4,14.8 L9.2,14.8 L9.2,20 L4,20 Z M14.8,14.8 L20,14.8 L20,20 L14.8,20 Z";

        private const string ListPath = "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18";

        /// <summary>Returns the geometry for an icon name, or empty geometry if unknown.</summary>
        public static Geometry Get(string name)
        {
            Geometry cached;
            if (Cache.TryGetValue(name, out cached)) return cached;

            Geometry geometry = Build(name);
            if (geometry != null) geometry.Freeze();
            Cache[name] = geometry;
            return geometry;
        }

        private static Geometry Build(string name)
        {
            switch (name)
            {
                case "vault":         return Join(VaultBody, VaultShackle, VaultDial);
                case "search":        return Join(SearchPath);
                case "plus":          return Join(PlusPath);
                case "minus":         return Join(MinusPath);
                case "star":          return Join(StarPath);
                case "note":          return Join(NotePath, NoteLines);
                case "key":           return Join(KeyPath, KeyTeeth);
                case "folder":        return Join(FolderPath);
                case "gear":          return Join(GearPath, GearHole);
                case "copy":          return Join(CopyBack, CopyFront);
                case "eye":           return Join(EyePath, EyeSlashCircle);
                case "eye-off":       return Join(EyeSlashPath, EyeSlashCircle);
                case "refresh":       return Join(RefreshPath);
                case "trash":         return Join(TrashPath);
                case "shield":        return Join(ShieldPath);
                case "warning":       return Join(WarningPath, WarningBang);
                case "check":         return Join(CheckPath);
                case "chevron-right": return Join(ChevronRightPath);
                case "chevron-left":  return Join(ChevronLeftPath);
                case "chevron-down":  return Join(ChevronDownPath);
                case "arrow-up":      return Join(ArrowUpPath);
                case "arrow-down":    return Join(ArrowDownPath);
                case "arrow-left":    return Join(ArrowLeftPath);
                case "arrow-right":   return Join(ArrowRightPath);
                case "sun":           return Join(Sun(), SunRays);
                case "moon":          return Join(MoonPath);
                case "tag":           return Join(TagPath, TagDot);
                case "clock":         return Join(Circle(12, 12, 8.6), ClockHands);
                case "download":      return Join(DownloadArrow, DownloadTray);
                case "upload":        return Join(UploadArrow, DownloadTray);
                case "close":         return Join(ClosePath);
                case "lock":          return Join(LockBody, LockShackle);
                case "unlock":        return Join(LockBody, UnlockShackle);
                case "link":          return Join(LinkLeft);
                case "globe":         return Join(Circle(12, 12, 8.6), GlobeMeridians);
                case "user":          return Join(UserHead, UserBody);
                case "database":      return Join(DbTop, DbBody, DbLines);
                case "info":          return Join(Circle(12, 12, 8.6), InfoDot, InfoBar);
                case "grid":          return Join(GridPath);
                case "list":          return Join(ListPath);
                case "wand":          return Join(WandStick, WandStar, WandSpark);
                case "history":       return Join(RefreshPath, ClockHands);
                default:              return Geometry.Empty;
            }
        }

        private const string SunRays =
            "M12,1.8 L12,4.2 M12,19.8 L12,22.2 M1.8,12 L4.2,12 M19.8,12 L22.2,12 " +
            "M4.7,4.7 L6.5,6.5 M17.5,17.5 L19.3,19.3 M19.3,4.7 L17.5,6.5 M6.5,17.5 L4.7,19.3";

        private static string Sun() { return Circle(12, 12, 4.2); }

        private static Geometry Join(params string[] paths)
        {
            if (paths == null || paths.Length == 0) return Geometry.Empty;

            GeometryGroup group = new GeometryGroup();
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    group.Children.Add(Geometry.Parse(path));
                }
                catch (FormatException)
                {
                    // A malformed glyph must not take the whole application down.
                }
            }
            group.Freeze();
            return group;
        }

        /// <summary>Minimal formatting helper so the geometry strings stay readable.</summary>
        private sealed class StringBuilderLike
        {
            private readonly System.Text.StringBuilder _sb = new System.Text.StringBuilder();

            public void Add(params object[] parts)
            {
                foreach (object part in parts)
                {
                    if (part is double) _sb.Append(N((double)part));
                    else if (part is string) _sb.Append((string)part);
                    else _sb.Append(part);
                }
            }

            public override string ToString() { return _sb.ToString(); }
        }
    }
}
