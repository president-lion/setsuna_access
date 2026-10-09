using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Control F11: the loaded game as plain text (setsuna_save.txt in the game folder), with names instead of the
    /// save file's raw ids. Everything the save holds that a person or another program would want: time, gold,
    /// story progress, place, party, each character's level, stats, equipment, spritnites and techs, the inventory,
    /// and the raw story flags and variables. Read from the same fields SaveDataManager.CreateSaveDataCommon writes.
    /// </summary>
    internal static class SaveDump
    {
        public static void Write()
        {
            var level = Application.loadedLevelName;
            if (level == "Boot" || level == "Logo" || level == "Title") { Speech.Say(Strings.SaveDumpNoGame); return; }
            var sdm = UnityEngine.Object.FindObjectOfType<SaveDataManager>();
            if (sdm == null) { Speech.Say(Strings.SaveDumpNoGame); return; }

            var sb = new StringBuilder();
            sb.AppendLine("I Am Setsuna - game state as of " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("(Written by the Setsuna Access mod from the loaded game; it matches the save file if dumped right after loading or saving.)");
            sb.AppendLine();

            var secs = GameManager.gamePlayTime;
            sb.AppendLine("Play time: " + (secs / 3600) + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00"));
            sb.AppendLine("Gold: " + Get<object>(sdm, "money"));
            sb.AppendLine("Story progress number: " + Get<object>(sdm, "eventProgression"));
            var floor = SceneManager.CurrentFloorInfo;
            if (floor != null) sb.AppendLine("Current place: " + TextClean.Clean(floor.mapName) + " (" + floor.id + ")");
            var saveFloor = Get<string>(sdm, "saveFloor");
            if (!string.IsNullOrEmpty(saveFloor))
                sb.AppendLine("Place stored in the save: " + MapData.FloorName(saveFloor) + " (" + saveFloor + "), position " + Get<object>(sdm, "savePosition"));
            var airship = Get<string>(sdm, "airShipName");
            if (!string.IsNullOrEmpty(airship)) sb.AppendLine("Airship name: " + airship);
            sb.AppendLine();

            var chars = ParameterManager.CharacterParameter;
            var field = Get<int[]>(sdm, "fieldMember");
            var party = Get<int[]>(sdm, "partyMember");
            sb.AppendLine("Active party: " + Members(field, chars));
            sb.AppendLine("All party members: " + Members(party, chars));
            sb.AppendLine();

            sb.AppendLine("== Characters ==");
            for (var i = 0; chars != null && i < chars.Count && i < 7; i++)
            {
                var c = chars[i];
                if (c == null) continue;
                var st = c.charaStatus;
                sb.AppendLine();
                sb.AppendLine(TextClean.Clean(c.Name) + " (character " + (i + 1) + ", " + st.memberState + ")");
                sb.AppendLine("  Level " + st.baseStatus.lv + ", EXP " + st.exp + ", HP " + c.Hp + " of " + st.baseStatus.maxHp + ", MP " + c.Mp + " of " + st.baseStatus.maxMp);
                sb.AppendLine("  Base stats: " + Fields(st.baseStatus));
                sb.AppendLine("  Weapon: " + ItemName(st.equip.weaponUniqueItemId));
                sb.AppendLine("  Talisman: " + ItemName(st.equip.accessoryUniqueItemId));
                var slots = st.materia.materiaSlot;
                if (slots != null)
                {
                    var used = new List<string>();
                    for (var k = 0; k < slots.Length; k++)
                        if (slots[k].slotType != SLOT_TYPE.SLOT_TYPE_NONE)
                            used.Add(SlotName(slots[k].slotType) + ": " + (slots[k].uniqueItemID > 0 ? ItemName(slots[k].uniqueItemID) : "empty"));
                    sb.AppendLine("  Spritnite slots: " + (used.Count > 0 ? string.Join("; ", used.ToArray()) : "none"));
                }
                if (c.commandSkillList != null && c.commandSkillList.Count > 0)
                {
                    var techs = new List<string>();
                    foreach (var s in c.commandSkillList) techs.Add(SkillName(s.skillId) + (s.isCoop ? " (combo)" : ""));
                    sb.AppendLine("  Techs: " + string.Join(", ", techs.ToArray()));
                }
            }
            sb.AppendLine();

            sb.AppendLine("== Inventory ==");
            var items = Get<HaveItemInfo[]>(sdm, "haveItemInfo");
            var byType = new SortedDictionary<string, List<string>>();
            if (items != null)
                foreach (var it in items)
                {
                    if (it == null || it.itemId <= 0 || it.haveNum == 0) continue;
                    ItemData data;
                    var known = ParameterManager.GetItemData(it.itemId, out data) && data != null;
                    var name = known ? TextClean.Clean(data.param.name) : "item " + it.itemId;
                    var type = known ? data.type.ToString() : "UNKNOWN";
                    var line = name + " x" + it.haveNum;
                    var flux = Flux(it);
                    if (flux.Length > 0) line += " (" + flux + ")";
                    List<string> list;
                    if (!byType.TryGetValue(type, out list)) byType[type] = list = new List<string>();
                    list.Add(line);
                }
            foreach (var kv in byType)
            {
                sb.AppendLine();
                sb.AppendLine(kv.Key + " (" + kv.Value.Count + "):");
                foreach (var l in kv.Value) sb.AppendLine("  " + l);
            }
            sb.AppendLine();

            sb.AppendLine("== Story flags (raw) ==");
            var flags = Get<int[]>(sdm, "eventFlag");
            if (flags != null)
            {
                var set = new List<string>();
                for (var i = 0; i < flags.Length; i++) if (flags[i] != 0) set.Add(i + "=" + flags[i]);
                sb.AppendLine("Non-zero event flag words (index=value), " + set.Count + " of " + flags.Length + ":");
                sb.AppendLine(string.Join(", ", set.ToArray()));
            }
            var vars = Get<float[]>(sdm, "eventVariable");
            if (vars != null)
            {
                var set = new List<string>();
                for (var i = 0; i < vars.Length; i++) if (vars[i] != 0f) set.Add(i + "=" + vars[i]);
                sb.AppendLine("Non-zero event variables (index=value), " + set.Count + ":");
                sb.AppendLine(string.Join(", ", set.ToArray()));
            }

            var path = Path.Combine(Path.GetDirectoryName(Application.dataPath), "setsuna_save.txt");
            File.WriteAllText(path, sb.ToString());
            Log.Info("SaveDump", path);
            Speech.Say(Strings.SaveDumped);
        }

        private static T Get<T>(object o, string field) { return Reflect.Get<T>(o, field); }

        private static string Members(int[] ids, List<CharacterParameter> chars)
        {
            if (ids == null || chars == null) return "unknown";
            var names = new List<string>();
            foreach (var id in ids)
                if (id > 0 && id <= chars.Count && chars[id - 1] != null) names.Add(TextClean.Clean(chars[id - 1].Name));
            return names.Count > 0 ? string.Join(", ", names.ToArray()) : "none";
        }

        private static string Fields(object o)
        {
            var parts = new List<string>();
            foreach (var f in o.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                parts.Add(f.Name + " " + f.GetValue(o));
            return string.Join(", ", parts.ToArray());
        }

        private static string ItemName(short uniqueId)
        {
            if (uniqueId <= 0) return "none";
            ItemData d;
            return ItemManager.GetItemDataFromUniqueId(uniqueId, out d) && d != null ? TextClean.Clean(d.param.name) : "item #" + uniqueId;
        }

        private static string SkillName(int id)
        {
            SkillData s;
            return ParameterManager.GetSkillData(id, out s) ? TextClean.Clean(s.name) : "skill " + id;
        }

        private static string SlotName(SLOT_TYPE t)
        {
            return t == SLOT_TYPE.SLOT_TYPE_COMMAND ? "command" : t == SLOT_TYPE.SLOT_TYPE_PASSIVE ? "support" : t == SLOT_TYPE.SLOT_TYPE_MULTI ? "either" : t.ToString();
        }

        /// <summary>Flux values on an item (HaveItemInfo.values: stat boosts and sublimation ids), when any are set.</summary>
        private static string Flux(HaveItemInfo it)
        {
            if (it.values == null) return "";
            var parts = new List<string>();
            for (var i = 0; i < it.values.Length; i++) if (it.values[i] != 0) parts.Add("value" + i + " " + it.values[i]);
            return parts.Count > 0 ? "flux " + string.Join(", ", parts.ToArray()) : "";
        }
    }
}
