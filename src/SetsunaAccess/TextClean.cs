using System.Text;
using System.Text.RegularExpressions;

namespace SetsunaAccess
{
    /// <summary>
    /// Turns display text into something worth speaking: strips Unity rich-text tags and
    /// leftover game tags, swaps the game's private font-icon glyphs (U+FFDC..U+FFFF) for words
    /// or drops them, and tidies whitespace. Pure logic, so it is unit tested.
    /// </summary>
    internal static class TextClean
    {
        // Unity rich text: <b>, </color>, <size=20>, <color=#fff>. Also the game's own unconverted
        // tags such as <ITEM=12> or full-width ＜...＞.
        private static readonly Regex Tags = new Regex(@"[<＜]/?[A-Za-z_][^<>＜＞]*[>＞]");
        private static readonly Regex Spaces = new Regex(@"[ \t　]+");
        private static readonly Regex Newlines = new Regex(@"\s*\n\s*");

        // UiCommon.FONT_*: icons live in the top of the BMP. Only these read as text.
        private const char Multiply = (char)65508, Copyright = (char)65503, Registered = (char)65502, Trademark = (char)65501;
        private const char IconLow = (char)65500;

        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text.Replace("\r", ""))
            {
                if (c < IconLow) { sb.Append(c); continue; }
                if (c == Multiply) sb.Append(" x ");
                else if (c == Copyright) sb.Append("©");
                else if (c == Registered) sb.Append("®");
                else if (c == Trademark) sb.Append("™");
                // other icons (item/weapon/menu glyphs) carry no words
            }
            var s = Tags.Replace(sb.ToString(), "");
            s = Newlines.Replace(s, " ");
            s = Spaces.Replace(s, " ");
            return s.Trim();
        }
    }
}
