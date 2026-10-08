using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Knowledge of maps the player isn't on: each floor's placement file is read straight out of
    /// parameter.cpk, decrypted like ParameterManager.DecryptParameter, and parsed with the game's
    /// own ObjectPlacementManager.CreatePlacementParameter (a pure function). Only groups the game
    /// would place right now count (same progress and flag test as ObjectPlacementManager.IsPlacement),
    /// so nothing later in the story leaks out.
    /// </summary>
    internal static class MapData
    {
        private static Cpk _cpk;
        private static bool _cpkFailed;
        private static readonly Dictionary<string, ObjectPlacementManager.PlacementParameter> _placements =
            new Dictionary<string, ObjectPlacementManager.PlacementParameter>(StringComparer.OrdinalIgnoreCase);

        public sealed class Summary
        {
            public readonly List<string> Shops = new List<string>();
            public readonly List<string> People = new List<string>();
            public bool SavePoint;
        }

        // ---- per floor ------------------------------------------------------------------

        public static FloorDataInfo Floor(string floorId)
        {
            FloorDataInfo f;
            return !string.IsNullOrEmpty(floorId) && ParameterManager.GetFloorData(floorId, out f) ? f : null;
        }

        public static string FloorName(string floorId)
        {
            var f = Floor(floorId);
            return f == null ? "" : TextClean.Clean(f.mapName);
        }

        /// <summary>Who is there and whether there's a save point, as the game would place them now.</summary>
        public static Summary Contents(string floorId)
        {
            var s = new Summary();
            ObjectPlacementManager.PlacementParameter p;
            if (!TryPlacement(floorId, out p)) return s;
            if (p.npcParameter != null)
                foreach (var g in p.npcParameter)
                {
                    if (!Placed(g.npcGroup.common) || g.npcParam == null) continue;
                    foreach (var n in g.npcParam)
                    {
                        var shop = ShopName(n.common.script);
                        if (shop.Length > 0 && !s.Shops.Contains(shop)) s.Shops.Add(shop);
                        NPCCharacter npc;
                        if (!ParameterManager.GetNPCCharacter(Common.CharacterIdToIndex(n.id), out npc) || npc == null) continue;
                        var name = TextClean.Clean(npc.name);
                        if (name.Length > 0 && !s.People.Contains(name)) s.People.Add(name);
                    }
                }
            if (p.savePointParameter != null)
                foreach (var g in p.savePointParameter)
                    if (Placed(g.savePointGroup.common) && g.savePointParam != null && g.savePointParam.Length > 0) s.SavePoint = true;
            return s;
        }

        /// <summary>Floors reachable through this floor's exits as placed now.</summary>
        public static List<string> Exits(string floorId)
        {
            var list = new List<string>();
            ObjectPlacementManager.PlacementParameter p;
            if (!TryPlacement(floorId, out p) || p.mapJumpParameter == null) return list;
            foreach (var g in p.mapJumpParameter)
            {
                if (!Placed(g.mapJumpGroup.common) || g.mapJumpParam == null) continue;
                foreach (var j in g.mapJumpParam)
                    if (!string.IsNullOrEmpty(j.jumpMapName) && !list.Contains(j.jumpMapName)) list.Add(j.jumpMapName);
            }
            return list;
        }

        /// <summary>
        /// Shopkeepers run ShopNpc_* scripts; the shop's own title (UI messages SHOP00-03, as
        /// UiShopResidentWindow shows it) names the kind of shop. "" for anyone else.
        /// </summary>
        public static string ShopName(string script)
        {
            string id;
            switch (script ?? "")
            {
                case "ShopNpc_Magic": id = "SHOP00"; break;
                case "ShopNpc_Item": id = "SHOP01"; break;
                case "ShopNpc_Cooking": id = "SHOP02"; break;
                case "ShopNpc_Accessory": id = "SHOP03"; break;
                default: return "";
            }
            string name;
            return ParameterManager.GetUIMessageData(id, out name) && name != null ? TextClean.Clean(name) : "";
        }

        // ---- search ---------------------------------------------------------------------

        /// <summary>
        /// Breadth-first over exits from <paramref name="from"/> to the closest floor with a save point.
        /// Returns the floor ids along the way (first = from), or null.
        /// </summary>
        public static List<string> PathToSavePoint(string from, int maxFloors = 200)
        {
            var prev = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            prev[from] = null;
            queue.Enqueue(from);
            while (queue.Count > 0 && prev.Count < maxFloors)
            {
                var cur = queue.Dequeue();
                // The world map lets you save from the menu anywhere (Common.IsEnableSave).
                if (Contents(cur).SavePoint || (IsWorldMap(cur) && EventManager.EventProgression <= 421010))
                {
                    var path = new List<string>();
                    for (var f = cur; f != null; f = prev[f]) path.Insert(0, f);
                    return path;
                }
                foreach (var next in Exits(cur))
                {
                    if (prev.ContainsKey(next)) continue;
                    prev[next] = cur;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        public static bool IsWorldMap(string floorId)
        {
            var f = Floor(floorId);
            return f != null && string.Equals(f.sceneName, Common.SCENE_NAME_WORLD_MAP, StringComparison.OrdinalIgnoreCase);
        }

        // ---- loading --------------------------------------------------------------------

        private static bool TryPlacement(string floorId, out ObjectPlacementManager.PlacementParameter p)
        {
            p = default(ObjectPlacementManager.PlacementParameter);
            var floor = Floor(floorId);
            if (floor == null || string.IsNullOrEmpty(floor.sceneName)) return false;
            if (_placements.TryGetValue(floor.sceneName, out p)) return true;
            try
            {
                var raw = Archive() == null ? null : _cpk.Read(floor.sceneName + "Placement");
                if (raw == null) return false;
                var bytes = Decrypt(raw);
                ObjectPlacementManager.CreatePlacementParameter(ref bytes, ref p);
                _placements[floor.sceneName] = p;
                return true;
            }
            catch (Exception ex)
            {
                Log.Once("MapData." + floor.sceneName, ex);
                return false;
            }
        }

        private static Cpk Archive()
        {
            if (_cpk != null || _cpkFailed) return _cpk;
            try { _cpk = new Cpk(Path.Combine(Application.streamingAssetsPath, "x86_64/parameter.cpk")); }
            catch (Exception ex) { _cpkFailed = true; Log.Once("MapData.cpk", ex); }
            return _cpk;
        }

        /// <summary>Same as ParameterManager.DecryptParameter.</summary>
        private static byte[] Decrypt(byte[] data)
        {
            using (var aes = new RijndaelManaged())
            {
                var t = aes.CreateDecryptor(Encoding.UTF8.GetBytes("8xTD|EgD|b?07QDj"), Encoding.UTF8.GetBytes("/]s@*CxLzM!9Qd%("));
                return t.TransformFinalBlock(data, 0, data.Length);
            }
        }

        /// <summary>ObjectPlacementManager.IsPlacement without its side effects.</summary>
        private static bool Placed(BaseGroupParams g)
        {
            if (!string.IsNullOrEmpty(g.invalidFlg) && g.invalidFlg != "-1" && EventManager.IsEventFlag(g.invalidFlg, false)) return false;
            if (g.minProgress < 0 && g.maxProgress < 0) return true;
            var prog = EventManager.EventProgression;
            if (prog < g.minProgress || prog > g.maxProgress) return false;
            if (!string.IsNullOrEmpty(g.enableFlg) && g.enableFlg != "-1" && !EventManager.IsEventFlag(g.enableFlg, false)) return false;
            return true;
        }
    }
}
