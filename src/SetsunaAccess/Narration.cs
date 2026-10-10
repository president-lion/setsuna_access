using System.Collections.Generic;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Describes what cutscenes show without words: characters appearing or fading away,
    /// gestures (nodding, kneeling, collapsing...), emotion bubbles, the screen shaking. Built
    /// from the event commands the Lua scripts call (EventControl), so it only says what is on
    /// screen at that moment. Lines are buffered and put in front of the next dialogue page, so
    /// the dialogue doesn't cut them off; anything left is spoken after a short pause.
    /// </summary>
    internal static class Narration
    {
        public static bool Enabled { get { return Settings.SceneDescriptions; } }

        private static readonly List<string> _pending = new List<string>();
        private static float _pendingSince;
        private static readonly Dictionary<string, float> _recent = new Dictionary<string, float>();

        // Gesture animations worth describing (one-shot or start-of-loop names only; talk and idle loops are skipped).
        private static readonly Dictionary<string, string> Gestures = new Dictionary<string, string>
        {
            { "s_yes", "nods" }, { "s_battleYes", "nods firmly" }, { "s_raiseYes", "nods" }, { "s_squatYes", "nods" },
            { "s_sitYes", "nods" }, { "s_raise2Yes", "nods" },
            { "s_no", "shakes their head" }, { "s_battleNo", "shakes their head firmly" }, { "s_squatNo", "shakes their head" },
            { "s_raiseNo", "shakes their head" }, { "s_raise2No", "shakes their head" },
            { "s_whyStart", "spreads their hands" }, { "s_whyLoop", "spreads their hands" },
            { "s_shoutStart", "shouts" }, { "s_thinkStart", "thinks" },
            { "s_squatStart", "kneels down" }, { "s_squatStart2", "kneels down" }, { "s_squatEnd", "stands up" },
            { "s_waistStart", "puts their hands on their hips" },
            { "s_handStart", "holds out a hand" },
            { "s_laugh", "laughs" },
            { "s_joyStart", "jumps for joy" }, { "s_frolicStart", "skips about happily" },
            { "s_guardStart", "braces themself" }, { "s_blockingStart", "blocks the way" },
            { "s_tiredStart", "slumps, exhausted" }, { "s_tiredStart2", "slumps, exhausted" }, { "s_battleTiredLoop", "is out of breath" },
            { "s_sitStart", "sits down" }, { "s_sitEnd", "gets up" },
            { "s_waveStart", "waves" },
            { "s_deathStart", "collapses" }, { "s_downStart", "falls down" }, { "s_faintLoop", "lies unconscious" },
            { "s_faint2Loop", "lies unconscious" },
            { "s_prayStart", "prays" }, { "s_angryStart", "gets angry" }, { "s_scaredStart", "cowers in fear" },
            { "s_hugStart", "hugs" }, { "s_poseStart", "strikes a pose" }, { "s_roar2Start", "roars" },
            { "s_takingStart", "reaches out" }, { "s_raiseStart", "raises a hand" }, { "s_raise2Start", "raises a hand" },
            { "f_backJumpStart", "jumps back" },
            { "b_idleStart", "readies for battle" }, { "b_enter", "readies for battle" },
            { "b_winStart", "strikes a victory pose" }, { "b_damage", "is hit" },
            { "b_attack_01", "attacks" }, { "b_attack_02", "attacks" }, { "b_attack_03", "attacks" }, { "b_attack_04", "attacks" },
            { "b_attackStart_01", "attacks" }, { "b_attackStart_02", "attacks" },
            { "b_magicStart_01", "casts a spell" }, { "b_magic_01", "casts a spell" }, { "b_magic_02", "casts a spell" },
            { "b_magic_03", "casts a spell" }, { "s_magicStart", "casts a spell" }, { "s_magicStart_01", "casts a spell" },
        };

        // CommonModule EMO_* effect ids: the bubble shown over a head.
        private static readonly Dictionary<string, string> Emotions = new Dictionary<string, string>
        {
            { "eff_ui_0004_01_01", "is startled" },
            { "eff_ui_0004_01_02", "looks puzzled" },
            { "eff_ui_0004_01_03", "chatters excitedly" },
            { "eff_ui_0004_01_04", "is delighted" },
            { "eff_ui_0004_01_05", "is flustered" },
            { "eff_ui_0004_01_06", "breaks into a nervous sweat" },
            { "eff_ui_0004_01_07", "is shocked" },
        };

        // ---- event hooks ----------------------------------------------------------------

        public static void OnAnimation(string charaId, string anim, int typeId)
        {
            string verb;
            if (!Enabled || anim == null || !Gestures.TryGetValue(anim, out verb)) return;
            var name = Name(Chara(charaId, typeId));
            if (name.Length > 0) Add(name + " " + verb + ".", name + anim);
        }

        public static void OnEffectAt(string effectId, Vector3 pos)
        {
            string verb;
            if (!Enabled || effectId == null || !Emotions.TryGetValue(effectId, out verb)) return;
            var name = Name(NearestCharacter(pos));
            if (name.Length > 0) Add(name + " " + verb + ".", name + effectId);
        }

        /// <summary>Prefix of EnableCharacter: only real changes on a visible screen.</summary>
        public static void OnEnable(string charaId, int val, int typeId)
        {
            if (!Enabled || ScreenDark()) return;
            var c = Chara(charaId, typeId);
            if (c == null) return;
            var rs = c.GetComponentsInChildren<Renderer>(true);
            var r = rs.Length > 0 ? rs[0] : null;
            var shown = r != null && r.gameObject.activeInHierarchy && r.enabled;
            var want = val != 0;
            if (shown == want) return;
            var name = Name(c);
            if (name.Length > 0) Add(want ? Strings.Appears(name) : Strings.Disappears(name), name + "vis");
        }

        /// <summary>Prefix of FadeCharacter (isFade > 0 fades in).</summary>
        public static void OnFade(string charaId, int isFade, int typeId)
        {
            if (!Enabled || ScreenDark()) return;
            var c = Chara(charaId, typeId);
            if (c == null || !c.gameObject.activeSelf) return;
            var changer = c.charaColorChanger;
            var renderer = changer == null ? null : changer.charaRenderer;
            var alpha = renderer != null ? renderer.Alpha : (isFade > 0 ? 0f : 1f);
            var appearing = isFade > 0;
            if (appearing == alpha > 0.5f) return;
            var name = Name(c);
            if (name.Length > 0) Add(appearing ? Strings.Appears(name) : Strings.FadesAway(name), name + "vis");
        }

        /// <summary>Prefix of FadeIn: when an event fades in from black, say who is standing there.</summary>
        public static void OnFadeIn()
        {
            if (!Enabled || !ScreenDark() || !InEvent()) return;
            var leader = Leader();
            if (leader == null) return;
            var names = new List<string>();
            foreach (var npc in Object.FindObjectsOfType<NPCControl>())
            {
                if (!Shown(npc)) continue;
                var d = npc.transform.position - leader.position;
                d.y = 0f;
                if (d.magnitude > 25f) continue;
                var n = Name(npc);
                if (n.Length > 0 && !names.Contains(n)) names.Add(n);
            }
            if (names.Count > 0) Add(Strings.Nearby(string.Join(", ", names.ToArray())), "nearby" + string.Join(",", names.ToArray()));
        }

        /// <summary>Prefix of MoveCharaToXZ: walking up to someone, or walking off.</summary>
        public static void OnMove(string charaId, float x, float z, int typeId)
        {
            if (!Enabled || ScreenDark()) return;
            var c = Chara(charaId, typeId);
            if (c == null || !Shown(c)) return;
            var name = Name(c);
            if (name.Length == 0) return;
            var dest = new Vector3(x, c.transform.position.y, z);
            var trip = dest - c.transform.position;
            trip.y = 0f;
            if (trip.magnitude < 1.5f) return;

            // Arriving next to someone else?
            BaseCharacter other = null;
            var bestD = 2.5f * 2.5f;
            foreach (var o in Object.FindObjectsOfType<BaseCharacter>())
            {
                if (o == c || !Shown(o)) continue;
                var d = o.transform.position - dest;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; other = o; }
            }
            var otherName = Name(other);
            if (otherName.Length > 0) { Add(Strings.WalksUpTo(name, otherName), name + "move" + otherName); return; }

            var leader = Leader();
            if (leader != null && c.transform != leader)
            {
                var before = c.transform.position - leader.position; before.y = 0f;
                var after = dest - leader.position; after.y = 0f;
                if (after.magnitude - before.magnitude > 4f) Add(Strings.WalksAway(name), name + "away");
            }
        }

        public static void OnShake()
        {
            if (Enabled && !ScreenDark()) Add(Strings.ScreenShakes, "shake");
        }

        // ---- output ---------------------------------------------------------------------

        /// <summary>Called by Dialogue before a page: the narration that leads into it.</summary>
        public static string TakePending()
        {
            if (_pending.Count == 0) return null;
            var s = string.Join(" ", _pending.ToArray());
            _pending.Clear();
            return s;
        }

        public static void LateTick()
        {
            if (_pending.Count > 0 && Time.unscaledTime - _pendingSince > 0.8f)
                Speech.Say(TakePending(), false);
        }

        private static void Add(string line, string key)
        {
            // The same gesture by the same character within a few seconds is one gesture (loops restart).
            float last;
            var now = Time.unscaledTime;
            if (_recent.TryGetValue(key, out last) && now - last < 3f) return;
            _recent[key] = now;
            if (_pending.Count == 0) _pendingSince = now;
            _pending.Add(line);
        }

        // ---- helpers --------------------------------------------------------------------

        private static bool InEvent()
        {
            try { return EventManager.IsEvent; } catch { return false; }
        }

        private static Transform Leader()
        {
            var m = FieldPartyManager.Member;
            return m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
        }

        /// <summary>Active, drawn and not faded out (the game hides characters with alpha 0).</summary>
        private static bool Shown(BaseCharacter c)
        {
            if (c == null || !c.gameObject.activeInHierarchy) return false;
            var changer = c.charaColorChanger;
            var r = changer == null ? null : changer.charaRenderer;
            return r == null || r.Alpha > 0.5f;
        }

        private static bool ScreenDark()
        {
            try { return GuiManager.BlackOut; } catch { return false; }
        }

        private static BaseCharacter Chara(string charaId, int typeId)
        {
            if (string.IsNullOrEmpty(charaId)) return null;
            return EventManager.GetEventCharaFromID(Common.CharacterIdToIndex(charaId), charaId, (TypeId)typeId);
        }

        public static string Name(BaseCharacter c)
        {
            if (c == null) return "";
            var npc = c as NPCControl;
            if (npc != null) return npc.npcChara == null ? "" : TextClean.Clean(npc.npcChara.name);
            var pc = c as PlayerControl;
            if (pc != null) return pc.charaParam == null ? "" : TextClean.Clean(pc.charaParam.Name);
            var param = Reflect.Get<CharacterParameter>(c, "charaParam");
            return param == null ? "" : TextClean.Clean(param.Name);
        }

        private static BaseCharacter NearestCharacter(Vector3 pos)
        {
            BaseCharacter best = null;
            var bestD = 4f; // emotion bubbles sit just above a head
            foreach (var c in Object.FindObjectsOfType<BaseCharacter>())
            {
                var d = c.transform.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude < bestD) { bestD = d.sqrMagnitude; best = c; }
            }
            return best;
        }
    }
}
