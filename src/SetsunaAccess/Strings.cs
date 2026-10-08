namespace SetsunaAccess
{
    /// <summary>Every line the mod speaks in its own words. Game text is read as the game shows it.</summary>
    internal static class Strings
    {
        public const string Loaded = "Setsuna Access loaded.";
        public static string FrameCap(int fps) { return "Frame rate capped at " + fps + "."; }
        public const string FrameCapOff = "Frame rate: game default.";
        public const string KeysReset = "Key config reset to the default keys.";
        public const string UiDumped = "UI dump written.";
        public const string Delete = "Delete save data";
        public const string Locked = "unavailable";
        public const string Unassigned = "not assigned";
        public const string NoSaveData = "No save data.";

        public const string KnockedOut = "knocked out";
        public const string ScreenShakes = "The screen shakes.";
        public const string NarrationOn = "Scene descriptions on.", NarrationOff = "Scene descriptions off.";
        public static string Nearby(string names) { return "Nearby: " + names + "."; }
        public static string WalksUpTo(string who, string to) { return who + " walks up to " + to + "."; }
        public static string WalksAway(string who) { return who + " walks away."; }
        public static string Appears(string name) { return name + " appears."; }
        public static string Disappears(string name) { return name + " disappears."; }
        public static string FadesAway(string name) { return name + " fades away."; }
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
            "F1 help. F2 frame rate cap, to lower CPU use. Control F2 rename the selected scanner object. Z location. F3 repeat. Page up and page down, previous and next nearby object. " +
            "Control page up and page down, scanner category. Home, where is the selected object. " +
            "Control home, walk to it; any movement key stops. End, beacon tone toward it. Shift end, show or hide unreachable things. P party status. N story objective, and selects it. T in battle, whose turn it is. L nearest save point, and selects the way there. V scene descriptions on or off. F11 screen text dump.";
        public static string DefaultName(string name) { return "Default: " + name; }
        public static string Deleted(string text) { return "deleted " + text; }
        public static string NameChosen(string name) { return name + "."; }
        public static string GameKeys(string confirm, string cancel, string menu, string momentum,
                                      string up, string left, string down, string right)
        {
            return "Game keys: confirm Enter or " + confirm + ", cancel " + cancel + ", main menu " + menu +
                   ", Momentum " + momentum + ", move " + up + " " + left + " " + down + " " + right +
                   ". Escape asks to quit the game.";
        }
        public const string WalkCancelled = "Stopped.";
        public const string WalkLost = "Target gone.";
        public static string WalkingTo(string name) { return "Walking to " + name; }
        public static string Arrived(string name) { return "Arrived at " + name; }
        public static string Blocked(string name) { return "Blocked, can't reach " + name; }
        public static string NoRouteTo(string name) { return "No way to " + name + " from here. It may be closed off for now."; }

        private static readonly string[] Directions = { "up", "up right", "right", "down right", "down", "down left", "left", "up left" };
        public static string Direction(int octant) { return Directions[octant & 7]; }
        public static string Distance(int meters) { return meters <= 1 ? "close" : meters + " meters"; }
        public static string Count(int n) { return n == 1 ? "1 thing" : n + " things"; }
        public static string NothingNearby(string category) { return "No " + category.ToLowerInvariant() + " here."; }
        public const string FilterOn = "Hiding things you can't walk to.";
        public const string FilterOff = "Showing everything, reachable or not.";
        public static string HiddenUnreachable(int n) { return n + " unreachable hidden"; }
        public const string NoPath = "No walkable path found, pointing straight.";
        public const string FindingWay = "Finding the way.";
        public static string PathInfo(int pathMeters, string direction, int legMeters)
        {
            return "Path " + pathMeters + " meters, head " + direction + " for " + System.Math.Max(1, legMeters) + " meters";
        }
        public static string SaveOnWorldMap(System.Collections.Generic.List<string> route)
        {
            var steps = route.Count == 1 ? "1 area away" : route.Count + " areas away";
            return "You can save from the menu on the world map, " + steps + ", through " + string.Join(", ", route.ToArray()) + ". Next:";
        }
        public const string ObjectiveSpot = "Objective: a spot to walk to";
        public const string ObjectiveUnknown = "No objective found here. Try talking to people or moving on.";
        public const string NextStep = "Next:";
        public static string ObjectiveTalk(string name) { return "Objective: talk to " + name; }
        public static string ObjectiveEnemy(string name)
        {
            return string.IsNullOrEmpty(name) ? "Objective: fight the monsters" : "Objective: fight " + name;
        }
        public static string ObjectiveGoTo(string place) { return "Objective: go to " + place; }
        public static string Through(System.Collections.Generic.List<string> route)
        {
            return "through " + string.Join(", ", route.ToArray());
        }
        public static string RenamePrompt(string current)
        {
            return "Rename " + current + ". Type a name and press Enter. Empty restores the original. Control F2 cancels.";
        }
        public static string Renamed(string name) { return "Named " + name + "."; }
        public const string RenameCleared = "Custom name removed.";
        public const string RenameCancelled = "Rename cancelled.";
        public const string Space = "space";
        public const string NoSavePointFound = "No save point found nearby.";
        public const string SavePointHere = "There's a save point in this area.";

        /// <summary>"Exit to Nive Village: Item Shop, Shopkeeper, Old Woman, save point".</summary>
        public static string ExitDetail(string exit, System.Collections.Generic.List<string> people, bool savePoint)
        {
            return ExitDetail(exit, new System.Collections.Generic.List<string>(), people, savePoint);
        }

        public static string ExitDetail(string exit, System.Collections.Generic.List<string> shops,
                                        System.Collections.Generic.List<string> people, bool savePoint)
        {
            var parts = new System.Collections.Generic.List<string>(shops);
            for (var i = 0; i < people.Count && i < 3; i++) parts.Add(people[i]);
            if (savePoint) parts.Add("save point");
            return parts.Count == 0 ? exit : exit + ": " + string.Join(", ", parts.ToArray());
        }

        public static string SavePointRoute(string destination, System.Collections.Generic.List<string> route)
        {
            var steps = route.Count == 1 ? "1 area away" : route.Count + " areas away";
            return "Nearest save point: " + destination + ", " + steps + ", through " + string.Join(", ", route.ToArray()) + ". Next:";
        }

        public static string Exit(string destination)
        {
            return string.IsNullOrEmpty(destination) ? "Exit" : "Exit to " + destination;
        }

        public static string Percent(int p) { return p + " percent"; }
        public static string Option(int index, int count) { return "option " + (index + 1) + " of " + count; }
        public static string WithShop(string person, string shop) { return person + ", " + shop; }
        public const string EquippedNow = "equipped";
        public const string SameStats = "same stats as equipped";
        public const string UpgradeTitle = "Upgrade.";
        public const string Upgraded = "Upgraded.";
        public static string Equipped(string name) { return "Equipped " + name; }
        private static readonly string[] StatNames = { "Attack", "Defense", "Magic attack", "Magic defense" };
        public static string StatChange(int stat, int from, int to) { return StatNames[stat] + " " + from + " to " + to; }
        public static string CountPrice(int count, string price) { return count + ", price " + price; }
        public static string Hp(int now, int max) { return "HP " + now + " of " + max; }
        public static string Mp(int now, int max) { return "MP " + now + " of " + max; }
        public static string Selecting(string name) { return "Choosing for " + name; }
        public static string AlsoReady(string names) { return "Also ready: " + names; }
        public const string NobodyReady = "Nobody is ready";
        public static string AtbFill(string name, int pct) { return name + " " + pct + " percent"; }
        public static string MomentumStock(int n) { return "Momentum " + n; }
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
