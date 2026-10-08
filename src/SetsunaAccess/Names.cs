using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace SetsunaAccess
{
    /// <summary>
    /// The player's own names for scanner objects. Keys identify an object on a map
    /// ("floorId|kind|objectName" plus a rounded position when the object can't move). Built-in names ship
    /// in the embedded CustomNames.txt (filled from players' names.txt files); the player's own
    /// UserData\SetsunaAccess\names.txt is loaded over them and is what renaming writes. Format: key TAB name.
    /// </summary>
    internal static class Names
    {
        private static Dictionary<string, string> _names;
        private static string File { get { return Path.Combine(Log.Dir, "names.txt"); } }

        public static string Get(string key)
        {
            Load();
            string n;
            return key != null && _names.TryGetValue(key, out n) ? n : null;
        }

        /// <summary>Set (or with an empty name, remove) a custom name and save the player's file.</summary>
        public static void Set(string key, string name)
        {
            Load();
            if (string.IsNullOrEmpty(name)) _names.Remove(key);
            else _names[key] = name;
            Save();
        }

        private static void Load()
        {
            if (_names != null) return;
            _names = new Dictionary<string, string>(StringComparer.Ordinal);
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                foreach (var res in asm.GetManifestResourceNames())
                    if (res.EndsWith("CustomNames.txt"))
                        using (var r = new StreamReader(asm.GetManifestResourceStream(res))) Parse(r.ReadToEnd());
            }
            catch (Exception ex) { Log.Once("Names.builtin", ex); }
            try { if (System.IO.File.Exists(File)) Parse(System.IO.File.ReadAllText(File)); }
            catch (Exception ex) { Log.Once("Names.load", ex); }
        }

        private static void Parse(string text)
        {
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var tab = line.IndexOf('\t');
                if (line.Length == 0 || line[0] == '#' || tab <= 0) continue;
                _names[line.Substring(0, tab)] = line.Substring(tab + 1);
            }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string> { "# Your names for scanner objects: key, a tab, then the name. Ctrl+F2 in game edits them." };
                var keys = new List<string>(_names.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) lines.Add(k + "\t" + _names[k]);
                System.IO.File.WriteAllText(File, string.Join("\r\n", lines.ToArray()) + "\r\n");
            }
            catch (Exception ex) { Log.Once("Names.save", ex); }
        }
    }
}
