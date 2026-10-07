namespace SetsunaAccess
{
    /// <summary>Every line the mod speaks in its own words. Game text is read as the game shows it.</summary>
    internal static class Strings
    {
        public const string Loaded = "Setsuna Access loaded.";
        public const string UiDumped = "UI dump written.";
        public const string Delete = "Delete save data";
        public const string Locked = "unavailable";
        public const string Unassigned = "not assigned";
        public const string NoSaveData = "No save data.";

        public const string KnockedOut = "knocked out";
        public const string GameOver = "Game over.";

        // Field scanner.
        public const string CatAll = "Everything", CatPeople = "People", CatChests = "Chests", CatExits = "Exits",
                            CatSavePoints = "Save points", CatSparkles = "Sparkles";
        public const string Person = "Person", Chest = "Chest", OpenedChest = "Opened chest",
                            SavePointName = "Save point", Sparkle = "Sparkle";
        public const string NothingSelected = "Nothing selected.";
        public const string BeaconOn = "Beacon on.", BeaconOff = "Beacon off.";
        public const string UnknownPlace = "Unknown place.";
        public const string Help =
            "F1 help. Z location. F3 repeat. Page up and page down, previous and next nearby object. " +
            "Control page up and page down, scanner category. Home, where is the selected object. " +
            "Control home, walk to it; any movement key stops. End, beacon tone toward it. F9 party status. F11 screen text dump.";
        public static string DefaultName(string name) { return "Default: " + name; }
        public static string Deleted(string text) { return "deleted " + text; }
        public static string NameChosen(string name) { return name + "."; }
        public const string WalkCancelled = "Stopped.";
        public const string WalkLost = "Target gone.";
        public static string WalkingTo(string name) { return "Walking to " + name; }
        public static string Arrived(string name) { return "Arrived at " + name; }
        public static string Blocked(string name) { return "Blocked, can't reach " + name; }

        private static readonly string[] Directions = { "up", "up right", "right", "down right", "down", "down left", "left", "up left" };
        public static string Direction(int octant) { return Directions[octant & 7]; }
        public static string Distance(int meters) { return meters <= 1 ? "close" : meters + " meters"; }
        public static string Count(int n) { return n == 1 ? "1 thing" : n + " things"; }
        public static string NothingNearby(string category) { return "No " + category.ToLowerInvariant() + " here."; }
        public static string Exit(string destination)
        {
            return string.IsNullOrEmpty(destination) ? "Exit" : "Exit to " + destination;
        }

        public static string CountPrice(int count, string price) { return count + ", price " + price; }
        public static string Hp(int now, int max) { return "HP " + now + " of " + max; }
        public static string Mp(int now, int max) { return "MP " + now + " of " + max; }
        public static string Ready(string name) { return name + " ready"; }
        public static string Cost(string cost) { return "Cost " + cost; }
        public static string EnemySkill(string skill) { return "Enemy: " + skill; }
        public static string MomentumCharged(string name, int stock) { return name + " Momentum " + stock; }
        public static string BattleStart(string enemies)
        {
            return string.IsNullOrEmpty(enemies) ? "Battle." : "Battle: " + enemies + ".";
        }

        /// <summary>Damage pop-ups. type is UiBattleWindow.HUD_TXT_TYPE by name.</summary>
        public static string Hud(string type, string target, string value)
        {
            string what;
            switch (type)
            {
                case "DAMAGE_HP": what = value + " damage"; break;
                case "DAMAGE_MP": what = value + " MP damage"; break;
                case "RECOVERY_HP": what = "recovers " + value + " HP"; break;
                case "RECOVERY_MP": what = "recovers " + value + " MP"; break;
                case "CRITICAL": what = "critical, " + value + " damage"; break;
                case "KILL": what = string.IsNullOrEmpty(value) ? "defeated" : value; break;
                default: what = value; break; // MISS, BUFF, DEBUFF: the game's own word or status name
            }
            if (string.IsNullOrEmpty(what)) return target;
            return string.IsNullOrEmpty(target) ? what : target + " " + what;
        }

        /// <summary>Unity KeyCode name as a person would say it: Alpha1 -> 1, LeftControl -> Left Control.</summary>
        public static string KeyName(string keyCode)
        {
            if (string.IsNullOrEmpty(keyCode) || keyCode == "None") return Unassigned;
            if (keyCode.StartsWith("Alpha") && keyCode.Length == 6) return keyCode.Substring(5);
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < keyCode.Length; i++)
            {
                if (i > 0 && char.IsUpper(keyCode[i]) && !char.IsUpper(keyCode[i - 1])) sb.Append(' ');
                sb.Append(keyCode[i]);
            }
            return sb.ToString();
        }

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
