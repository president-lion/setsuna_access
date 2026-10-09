using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// N: where the story wants you to go next. Reads the game's own GameFlow table (as
    /// EventManager.CheckMainPtathEvent does) for entries at the current story progress; each names
    /// what starts the next event: talking to someone (trigger 1, terms = their object name), entering a
    /// trigger zone (2, terms = EventCollision id), a battle ending (terms = enemy group id), or arriving
    /// on a map (5, terms = floor id). The target is found in this area and selected for Home / Control
    /// Home / the beacon, or else the map that holds it is named and its first exit selected.
    /// </summary>
    internal static class Objective
    {
        private const byte TalkTrigger = 1, EnterTrigger = 2, FloorTrigger = 5;

        private sealed class Step
        {
            public byte Trigger;
            public string Terms;
            public string EventFile;
        }

        public static void Find()
        {
            if (!Application.loadedLevelName.Contains("_") || UiCampManager.IsShowing) return;
            var steps = CurrentSteps();
            Log.Append("nav.log", "objective: progress " + EventManager.EventProgression + ", " + steps.Count + " steps: "
                                  + string.Join("; ", steps.ConvertAll(s => s.Trigger + "/" + s.Terms + "/" + s.EventFile).ToArray()));

            // Something in this area first.
            foreach (var s in steps)
                if (TryHere(s)) return;

            // Elsewhere: the nearest map that holds the target. This comes before any stray monster: in
            // Serendale, after the story's battle was won, N sent the party at a leftover field monster
            // behind a barrier while the story wanted the next map.
            foreach (var s in steps)
                if (TryElsewhere(s)) return;

            // Nothing known: monsters around may be what's in the way (only when the table names no battle
            // group we could look for and no map).
            if (steps.Count == 0)
            {
                var enemy = NearestEnemy(null);
                if (enemy != null)
                {
                    Field.SetObjective(enemy.transform, Strings.ObjectiveEnemy(Narration.Name(enemy)), true);
                    return;
                }
            }

            Speech.Say(Strings.ObjectiveUnknown);
        }

        // ---- this area ------------------------------------------------------------------

        private static string NpcIdOf(BaseCharacter c)
        {
            var npc = c as NPCControl;
            return npc == null || npc.npcParam == null ? null : npc.npcParam.id;
        }

        private static bool TryHere(Step s)
        {
            switch (s.Trigger)
            {
                case TalkTrigger:
                    var alt = MapData.NpcIndexId(s.Terms);
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<BaseCharacter>())
                        if (c.gameObject.activeInHierarchy && (c.name == s.Terms || (alt != null && (c.name == alt || NpcIdOf(c) == alt))))
                        {
                            var name = Narration.Name(c);
                            return Here(c.transform, Strings.ObjectiveTalk(name.Length > 0 ? name : Strings.Person), false);
                        }
                    return false;
                case EnterTrigger:
                    foreach (var e in UnityEngine.Object.FindObjectsOfType<EventCollision>())
                        if (e.gameObject.activeInHierarchy && e.param.id == s.Terms)
                        {
                            return Here(e.transform, Strings.ObjectiveSpot, true);
                        }
                    return false;
                case FloorTrigger:
                    return false;
                default:
                    // A battle ending: the enemy group the story is waiting for.
                    var enemy = NearestEnemy(s.Terms);
                    if (enemy == null) return false;
                    return Here(enemy.transform, Strings.ObjectiveEnemy(Narration.Name(enemy)), true);
            }
        }

        /// <summary>Select the target here, or, if it's in a part of this map you can't walk to, the way round.</summary>
        private static bool Here(Transform t, string name, bool walkInto)
        {
            if (OtherPart(t, name)) return true;
            _arriveFloor = _arrivePoint = null; // reached the right part
            Field.SetObjective(t, name, walkInto);
            return true;
        }

        // Set when the objective is in a part of a split map entered at one particular arrival point;
        // TryElsewhere then routes into that floor only through that entrance.
        private static string _arriveFloor, _arrivePoint;

        /// <summary>
        /// Some maps are split into parts you enter separately (Serendale has two entrances from the world map
        /// and its parts don't connect inside). When the reachability fill says the target can't be walked to
        /// from here, find the arrival point nearest the target among those you can't reach either, the
        /// neighbouring floor whose exit leads there, and select the way out to that floor; Field then prefers
        /// that exact entrance there.
        /// </summary>
        private static bool OtherPart(Transform t, string name)
        {
            var player = Leader();
            var floor = SceneManager.CurrentFloorInfo;
            if (player == null || floor == null) return false;
            if (Nav.CanReach(player.position, t.position, 3f) != Nav.Reach.No) return false;

            MapData.Jump arrival = null;
            var best = float.MaxValue;
            foreach (var a in MapData.Jumps(floor.id, true))
            {
                if (Nav.CanReach(player.position, a.Pos, 2f) != Nav.Reach.No) continue;
                var d = a.Pos - t.position; d.y = 0f;
                if (d.sqrMagnitude < best) { best = d.sqrMagnitude; arrival = a; }
            }
            if (arrival == null) { Log.Append("nav.log", "objective: target unreachable here and no other arrival point"); return Switch(player, name); }

            // Out through an exit we can walk to, round to a floor whose exit enters this one at that arrival point.
            var here = floor.id;
            var path = MapData.PathTo(here, f => string.Equals(f, here, StringComparison.OrdinalIgnoreCase),
                                      j => Nav.CanReach(player.position, j.Pos, 3f) != Nav.Reach.No, here, arrival.Id);
            if (path != null && path.Count >= 3)
            {
                Log.Append("nav.log", "objective: " + t.name + " is in another part of " + here + "; arrival " + arrival.Id + " "
                                      + NavLog.P(arrival.Pos) + "; route " + string.Join(" > ", path.ToArray()));
                _arriveFloor = here;
                _arrivePoint = arrival.Id;
                Field.PreferEntrance(here, arrival.Id);
                Field.SelectRoute(path, Strings.ObjectiveOtherPart(name, MapData.FloorName(here), MapData.FloorName(path[path.Count - 2])));
                return true;
            }
            Log.Append("nav.log", "objective: target unreachable here; arrival " + arrival.Id + " has no known entrance");
            return Switch(player, name);
        }

        /// <summary>
        /// The target is cut off but a switch you can reach hasn't been used: that's most likely the way
        /// (Serendale's bridge east). Select the nearest such switch.
        /// </summary>
        private static bool Switch(Transform player, string name)
        {
            GimmickSwitch best = null;
            var bestD = float.MaxValue;
            foreach (var g in UnityEngine.Object.FindObjectsOfType<GimmickSwitch>())
            {
                if (!g.gameObject.activeInHierarchy || !Reflect.Get<bool>(g, "isPower") || Reflect.Get<bool>(g, "isOn")) continue;
                if (Nav.CanReach(player.position, g.transform.position, 1.5f) == Nav.Reach.No) continue;
                var d = (g.transform.position - player.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = g; }
            }
            if (best == null) return false;
            Log.Append("nav.log", "objective: cut off; suggesting switch " + best.name + " " + NavLog.P(best.transform.position));
            Field.SelectSwitch(best, Strings.ObjectiveViaSwitch(name));
            return true;
        }

        private static Transform Leader()
        {
            var m = FieldPartyManager.Member;
            return m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
        }

        private static EnemyControl NearestEnemy(string groupId)
        {
            var m = FieldPartyManager.Member;
            var leader = m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
            if (leader == null) return null;
            EnemyControl best = null;
            var bestD = float.MaxValue;
            foreach (var e in UnityEngine.Object.FindObjectsOfType<EnemyControl>())
            {
                if (!e.gameObject.activeInHierarchy) continue;
                if (groupId != null && (e.group == null || e.group.id != groupId)) continue;
                var d = (e.transform.position - leader.position).sqrMagnitude;
                if (d < bestD && d < 1000f * 1000f) { bestD = d; best = e; }
            }
            return best;
        }

        // ---- another map ----------------------------------------------------------------

        private static bool TryElsewhere(Step s)
        {
            var floor = SceneManager.CurrentFloorInfo;
            if (floor == null) return false;
            Func<string, bool> holds;
            switch (s.Trigger)
            {
                case TalkTrigger: holds = f => MapData.HasNpc(f, s.Terms); break;
                case EnterTrigger: holds = f => MapData.HasTriggerZone(f, s.Terms); break;
                case FloorTrigger: holds = f => string.Equals(f, s.Terms, StringComparison.OrdinalIgnoreCase); break;
                default: holds = f => MapData.HasEnemyGroup(f, s.Terms); break;
            }
            var player = Leader();
            Func<MapData.Jump, bool> reachable = j => player == null || Nav.CanReach(player.position, j.Pos, 3f) != Nav.Reach.No;
            var path = MapData.PathTo(floor.id, holds, reachable, _arriveFloor, _arrivePoint)
                       ?? MapData.PathTo(floor.id, holds);
            Log.Append("nav.log", "objective elsewhere: " + s.Trigger + "/" + s.Terms + " from " + floor.id + " -> "
                                  + (path == null ? "no map path" : string.Join(" > ", path.ToArray())));
            if (path == null || path.Count < 2) return false;
            Field.SelectRoute(path, Strings.ObjectiveGoTo(MapData.FloorName(path[path.Count - 1])));
            return true;
        }

        // ---- GameFlow -------------------------------------------------------------------

        private static List<Step> CurrentSteps()
        {
            var list = new List<Step>();
            byte[] data;
            if (!ParameterManager.GetParameter("GameFlow", out data) || data == null) return list;
            var size = Marshal.SizeOf(typeof(GameFlowInfo));
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                var progress = EventManager.EventProgression;
                for (var i = 0; i + size <= data.Length;)
                {
                    Marshal.Copy(data, i, ptr, size);
                    var info = (GameFlowInfo)Marshal.PtrToStructure(ptr, typeof(GameFlowInfo));
                    if (info.dataSize <= 0) break;
                    // -1 entries apply at any progress (shops, rests); only the story's next step counts here.
                    if (info.startProgress == progress && !string.IsNullOrEmpty(info.terms) && info.terms != "-1")
                        list.Add(new Step { Trigger = info.trigger, Terms = info.terms, EventFile = info.eventFile });
                    i += info.dataSize;
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return list;
        }
    }
}
