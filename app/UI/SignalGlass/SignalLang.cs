using System.Globalization;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// Language inventory and switching for Signal Glass.
    /// Uses the actual 22+1 repository locales and autonyms (no country flags).
    /// </summary>
    public static class SignalLang
    {
        public static IReadOnlyList<string> Codes => Nebula.NebulaLang.Codes;

        public static string Current => Nebula.NebulaLang.Current;

        public static string Badge(string code) =>
            string.IsNullOrEmpty(code) ? "◎" : code.ToUpperInvariant();

        public static string Autonym(string code) => Nebula.NebulaLang.Name(code);

        public static IEnumerable<(string code, string badge, string autonym, bool isCurrent)> Entries()
        {
            string cur = Current;
            foreach (string code in Nebula.NebulaLang.MenuOrder())
            {
                yield return (code, Badge(code), Autonym(code), string.Equals(cur, code, StringComparison.OrdinalIgnoreCase));
            }
        }

        public static void Apply(string code)
        {
            Nebula.NebulaLang.Apply(code);
            try { Program.signalForm?.OnLanguageChanged(); } catch { }
            try { Program.signalFlyout?.Invalidate(); } catch { }
        }
    }
}
