namespace SetsunaAccess
{
    /// <summary>Every line the mod speaks in its own words. Game text is read as the game shows it.</summary>
    internal static class Strings
    {
        public const string Loaded = "Setsuna Access loaded.";
        public const string UiDumped = "UI dump written.";
        public const string Delete = "Delete save data";

        /// <summary>"New Game, 1 of 2".</summary>
        public static string Item(string label, int index, int count)
        {
            if (count <= 1) return label;
            return label + ", " + (index + 1) + " of " + count;
        }

        /// <summary>"Setsuna: Hello." - no prefix when the speaker is unknown.</summary>
        public static string Line(string speaker, string text)
        {
            if (string.IsNullOrEmpty(speaker)) return text;
            return speaker + ": " + text;
        }
    }
}
