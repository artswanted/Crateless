using System.Globalization;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// The languages the app ships and the machinery to switch between them: the list, the flag
    /// picture, the name as that language writes it, and applying a choice without a restart.
    /// </summary>
    public static class NebulaLang
    {
        /// <summary>Culture codes with a translation. "" follows Windows.</summary>
        public static readonly string[] Codes =
        {
            "", "en", "ar", "cs-CZ", "da", "de", "es", "fr", "hu", "id", "it", "ja", "ko", "lt", "pl",
            "pt-BR", "pt-PT", "ro", "ru", "tr", "uk", "vi", "zh-CN", "zh-TW",
        };

        public static string Current => AppConfig.GetString("language") ?? "";

        /// <summary>The entry matching Windows, or null when this system's language is not one of ours.</summary>
        public static string? SystemLanguage()
        {
            var ui = CultureInfo.InstalledUICulture;
            foreach (var l in Codes)
                if (l.Length > 0 && string.Equals(l, ui.Name, StringComparison.OrdinalIgnoreCase)) return l;
            foreach (var l in Codes)
                if (l.Length > 0 && string.Equals(l.Split('-')[0], ui.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Menu order: follow Windows, then this system's language, then English, then the rest by name.</summary>
        public static List<string> MenuOrder()
        {
            var sys = SystemLanguage();
            var head = new List<string> { "" };
            if (sys is not null && sys != "en") head.Add(sys);
            head.Add("en");
            head.AddRange(Codes.Where(l => l.Length > 0 && !head.Contains(l)).OrderBy(Name, StringComparer.InvariantCulture));
            return head;
        }

        public static string Name(string code)
        {
            if (code.Length == 0)
            {
                string sys = SystemLanguage() is string s ? Name(s) : "English";
                return NebulaText.T("System", "Системный") + " · " + sys;
            }
            try
            {
                var name = CultureInfo.GetCultureInfo(code).NativeName;
                if (name.Length > 0) name = char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
                // "português (Brasil)" keeps the country because there are two of them; "čeština (Česko)" does not need one
                int bracket = name.IndexOf(" (", StringComparison.Ordinal);
                string two = code.Split('-')[0];
                bool severalVariants = Codes.Count(l => l.Length > 0 && l.Split('-')[0] == two) > 1;
                if (bracket > 0 && !severalVariants) name = name[..bracket];
                return name;
            }
            catch { return code; }
        }

        public static string FlagName(string code) => code.Length == 0 ? "system" : code switch
        {
            "cs-CZ" => "cs",
            _ => code,
        };

        public static Image? Flag(string code, int pixels) => NebulaAssets.Scaled("flag-" + FlagName(code), Math.Max(8, pixels));

        /// <summary>
        /// Switches language straight away: the drawn interface re-reads its table and repaints, and
        /// resource lookups follow the new culture. Classic windows that are already open keep the
        /// language they were built with until they are reopened.
        /// </summary>
        public static void Apply(string code)
        {
            AppConfig.Set("language", code);
            try
            {
                var culture = code.Length > 0 ? CultureInfo.GetCultureInfo(code) : CultureInfo.InstalledUICulture;
                Thread.CurrentThread.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                Logger.WriteLine("Language: " + (code.Length > 0 ? code : "system (" + culture.Name + ")"));
            }
            catch (Exception ex) { Logger.WriteLine("Language: " + ex.Message); }

            NebulaText.Reload();
            try { Program.modeControl?.SetModeLabel(); } catch { }
            try { Program.nebulaForm?.Invalidate(); } catch { }
            try { Program.nebulaCompact?.Invalidate(); } catch { }
        }

        /// <summary>The entries for a language menu, each with its flag.</summary>
        public static IEnumerable<(string text, bool isChecked, Image? image, Action action)> MenuItems(Action? after = null)
        {
            string cur = Current;
            foreach (var code in MenuOrder())
            {
                string c = code;
                yield return (Name(c), cur == c, Flag(c, 20), () => { Apply(c); after?.Invoke(); });
            }
        }
    }
}
