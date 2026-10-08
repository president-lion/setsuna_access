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

            // Monsters standing in the way of the story (e.g. a battle that has to be won).
            var enemy = NearestEnemy(null);
            if (enemy != null)
            {
                Field.SetObjective(enemy.transform, Strings.ObjectiveEnemy(Narration.Name(enemy)), true);
                return;
            }

            // Elsewhere: the nearest map that holds the target.
            foreach (var s in steps)
                if (TryElsewhere(s)) return;

            Speech.Say(Strings.ObjectiveUnknown);
        }

        // ---- this area ------------------------------------------------------------------

        private static bool TryHere(Step s)
        {
            switch (s.Trigger)
            {
                case TalkTrigger:
                    foreach (var c in UnityEngine.Object.FindObjectsOfType<BaseCharacter>())
                        if (c.gameObject.activeInHierarchy && c.name == s.Terms)
                        {
                            var name = Narration.Name(c);
                            Field.SetObjective(c.transform, Strings.ObjectiveTalk(name.Length > 0 ? name : Strings.Person), false);
                            return true;
                        }
                    return false;
                case EnterTrigger:
                    foreach (var e in UnityEngine.Object.FindObjectsOfType<EventCollision>())
                        if (e.gameObject.activeInHierarchy && e.param.id == s.Terms)
                        {
                            Field.SetObjective(e.transform, Strings.ObjectiveSpot, true);
                            return true;
                        }
                    return false;
                case FloorTrigger:
                    return false;
                default:
                    // A battle ending: the enemy group the story is waiting for.
                    var enemy = NearestEnemy(s.Terms);
                    if (enemy == null) return false;
                    Field.SetObjective(enemy.transform, Strings.ObjectiveEnemy(Narration.Name(enemy)), true);
                    return true;
            }
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
            var path = MapData.PathTo(floor.id, holds);
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
