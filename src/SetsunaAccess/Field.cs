using System.Collections.Generic;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Field exploration: place names on arrival, and a scanner over nearby people, chests,
    /// exits, save points and sparkle spots. Directions are screen-relative (up = away from
    /// the camera), matching W/A/S/D. An optional beacon beeps toward the selected object.
    /// </summary>
    internal static class Field
    {
        private enum Kind { Person, Chest, Exit, SavePoint, Sparkle }

        private sealed class Target
        {
            public Kind Kind;
            public Transform Transform;
            public string Name;
            public float Distance;
        }

        // Category 0 = everything, then one per Kind.
        private static readonly string[] Categories =
            { Strings.CatAll, Strings.CatPeople, Strings.CatChests, Strings.CatExits, Strings.CatSavePoints, Strings.CatSparkles };

        private const float MaxRange = 1000f;

        private static int _category;
        private static int _index = -1;
        private static Transform _selected;
        private static string _selectedName;
        private static Kind _selectedKind;
        private static string _lastTelop;
        private static bool _beacon;
        private static float _nextBeep;

        // ---- place names ----------------------------------------------------------------

        public static void OnTelop(string text)
        {
            var s = TextClean.Clean(text);
            if (s.Length == 0) return;
            _lastTelop = s;
            Speech.Say(s, false);
        }

        public static void OnFloorReady(FloorDataInfo floor)
        {
            AutoWalk.Stop(null);
            _selected = null;
            _index = -1;
            var name = floor == null ? "" : TextClean.Clean(floor.mapName);
            if (name.Length == 0) return;
            if (name == _lastTelop) { _lastTelop = null; return; }
            _lastTelop = null;
            Speech.Say(name, false);
        }

        public static void SayLocation()
        {
            if (!InField()) return;
            var name = TextClean.Clean(SceneManager.CurrentFloorInfo == null ? "" : SceneManager.CurrentFloorInfo.mapName);
            Speech.Say(name.Length > 0 ? name : Strings.UnknownPlace);
        }

        public static void SayParty()
        {
            if (!InField()) return;
            var m = FieldPartyManager.Member;
            if (m == null) return;
            var parts = new List<string>();
            foreach (var pc in m)
            {
                if (pc == null || pc.charaParam == null) continue;
                var p = pc.charaParam;
                parts.Add(TextClean.Clean(p.Name) + ", " + Strings.Hp(p.Hp, p.MaxHp) + ", " + Strings.Mp(p.Mp, p.MaxMp));
            }
            Speech.Say(string.Join(". ", parts.ToArray()));
        }

        // ---- scanner --------------------------------------------------------------------

        public static void NextCategory(int step)
        {
            _category = (_category + step + Categories.Length) % Categories.Length;
            _index = -1;
            var list = Scan();
            Speech.Say(Categories[_category] + ", " + Strings.Count(list.Count));
        }

        public static void Cycle(int step)
        {
            if (!InField()) return;
            var list = Scan();
            if (list.Count == 0) { Speech.Say(Strings.NothingNearby(Categories[_category])); return; }
            // Keep the same object selected even if the distance order changed.
            var cur = _selected == null ? -1 : list.FindIndex(t => t.Transform == _selected);
            _index = cur < 0 ? (step > 0 ? 0 : list.Count - 1) : (cur + step + list.Count) % list.Count;
            var t0 = list[_index];
            _selected = t0.Transform;
            _selectedName = t0.Name;
            _selectedKind = t0.Kind;
            Speech.Say(Strings.Item(Describe(t0.Name, t0.Transform), _index, list.Count));
        }

        public static void RepeatSelected()
        {
            if (!InField()) return;
            if (_selected == null || !_selected.gameObject.activeInHierarchy) { Speech.Say(Strings.NothingSelected); return; }
            Speech.Say(Describe(_selectedName, _selected));
        }

        public static void WalkToSelected()
        {
            if (!InField()) return;
            if (AutoWalk.Active) { AutoWalk.Stop(Strings.WalkCancelled); return; }
            if (_selected == null || !_selected.gameObject.activeInHierarchy) { Speech.Say(Strings.NothingSelected); return; }
            // Stop where the game lets you interact; walk right into exits.
            float arrive;
            switch (_selectedKind)
            {
                case Kind.Person: arrive = 1.6f; break;
                case Kind.Chest: arrive = 1.3f; break;
                case Kind.SavePoint: arrive = 1.0f; break;
                case Kind.Sparkle: arrive = 0.7f; break;
                default: arrive = 0.2f; break;
            }
            AutoWalk.Start(_selected, _selectedName, arrive);
        }

        public static void ToggleBeacon()
        {
            _beacon = !_beacon;
            Speech.Say(_beacon ? Strings.BeaconOn : Strings.BeaconOff);
        }

        // Bump detection: the player is pushing a direction but the leader isn't moving.
        private static Vector3 _bumpPos;
        private static float _bumpSince = -1f, _nextBump;

        private static void BumpTick()
        {
            if (!InField()) return;
            var gs = GameManager.NowGameState;
            if ((gs != GAME_STATE.FIELD && gs != GAME_STATE.WORLD) || EventManager.IsEvent || UiCampManager.IsShowing
                || GuiManager.IsShowingMessageWindow || UiShopManager.IsShowing)
            {
                _bumpSince = -1f;
                return;
            }
            var leader = Player();
            var pushing = InputManager.Horizontal != 0f || InputManager.Vertical != 0f;
            if (leader == null || !pushing) { _bumpSince = -1f; return; }

            var now = Time.unscaledTime;
            if (_bumpSince < 0f) { _bumpSince = now; _bumpPos = leader.position; return; }
            if (now - _bumpSince < 0.25f) return;

            var moved = leader.position - _bumpPos;
            moved.y = 0f;
            if (moved.magnitude < 0.08f && now >= _nextBump)
            {
                Tones.Bump();
                _nextBump = now + 0.45f;
            }
            _bumpSince = now;
            _bumpPos = leader.position;
        }

        public static void Tick()
        {
            BumpTick();
            if (!_beacon || _selected == null || Time.unscaledTime < _nextBeep) return;
            if (!InField() || !_selected.gameObject.activeInHierarchy) return;
            var gs = GameManager.NowGameState;
            if (gs != GAME_STATE.FIELD && gs != GAME_STATE.WORLD) return;

            var player = Player();
            if (player == null) return;
            Vector2 screen;
            var dist = Relative(player.position, _selected.position, out screen);
            if (dist < 1.5f) { _nextBeep = Time.unscaledTime + 1.5f; Tones.Beacon(0f, 2f); return; }
            // Pan by left/right; higher pitch when the object is up-screen, lower when down.
            var dir = screen.normalized;
            Tones.Beacon(Mathf.Clamp(dir.x, -1f, 1f), Mathf.Lerp(0.75f, 1.35f, (dir.y + 1f) / 2f));
            _nextBeep = Time.unscaledTime + Mathf.Clamp(dist / 12f, 0.25f, 1.2f);
        }

        // ---- helpers --------------------------------------------------------------------

        private static bool InField()
        {
            // Field maps are named like "ma_0001_01"; Boot, Logo, Title, Empty are not.
            return Application.loadedLevelName.Contains("_");
        }

        private static Transform Player()
        {
            var m = FieldPartyManager.Member;
            return m != null && m.Count > 0 && m[0] != null ? m[0].transform : null;
        }

        private static string Describe(string name, Transform t)
        {
            var player = Player();
            if (player == null) return name;
            Vector2 screen;
            var dist = Relative(player.position, t.position, out screen);
            return name + ", " + Strings.Direction(Octant(screen)) + ", " + Strings.Distance(Mathf.RoundToInt(dist));
        }

        /// <summary>Ground distance, and the offset in screen terms (x = right, y = up/away).</summary>
        private static float Relative(Vector3 from, Vector3 to, out Vector2 screen)
        {
            var d = to - from;
            d.y = 0f;
            // Same basis the game moves the player in (BaseObject.CreateMoveVec).
            var right = MainCameraControl.cameraTransform.right; right.y = 0f; right.Normalize();
            var fwd = Vector3.Cross(right, Vector3.up).normalized;
            screen = new Vector2(Vector3.Dot(d, right), Vector3.Dot(d, fwd));
            return d.magnitude;
        }

        /// <summary>0 = up, 1 = up right ... 7 = up left.</summary>
        private static int Octant(Vector2 v)
        {
            var angle = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg; // 0 = up, 90 = right
            if (angle < 0) angle += 360f;
            return Mathf.RoundToInt(angle / 45f) % 8;
        }

        private static List<Target> Scan()
        {
            var list = new List<Target>();
            var player = Player();
            if (player == null) return list;
            var want = _category == 0 ? (Kind?)null : (Kind)(_category - 1);

            if (want == null || want == Kind.Person)
                foreach (var n in Object.FindObjectsOfType<NPCControl>())
                {
                    var nm = n.npcChara == null ? "" : TextClean.Clean(n.npcChara.name);
                    Add(list, Kind.Person, n.transform, nm.Length > 0 ? nm : Strings.Person, player);
                }
            if (want == null || want == Kind.Chest)
                foreach (var c in Object.FindObjectsOfType<ItemBox>())
                    Add(list, Kind.Chest, c.transform, Reflect.Get<bool>(c, "isOn") ? Strings.OpenedChest : Strings.Chest, player);
            if (want == null || want == Kind.Exit)
                foreach (var e in ExitLabels())
                    Add(list, Kind.Exit, e.Key.transform, e.Value, player);
            if (want == null || want == Kind.SavePoint)
                foreach (var s in Object.FindObjectsOfType<SavePoint>())
                    Add(list, Kind.SavePoint, s.transform, Strings.SavePointName, player);
            if (want == null || want == Kind.Sparkle)
                foreach (var p in Object.FindObjectsOfType<ShiningPoint>())
                    if (Reflect.Get<bool>(p, "isPopItem") && !Reflect.Get<bool>(p, "isItemGet"))
                        Add(list, Kind.Sparkle, p.transform, Strings.Sparkle, player);

            list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            return list;
        }

        /// <summary>
        /// Exits named by where they lead. Houses share their village's name, so when a name repeats
        /// (or is the place you're already in) the label adds who is there and any save point,
        /// read from the destination's placement data.
        /// </summary>
        private static List<KeyValuePair<MapJump, string>> ExitLabels()
        {
            var jumps = new List<MapJump>();
            foreach (var j in Object.FindObjectsOfType<MapJump>())
                if (j.gameObject.activeInHierarchy) jumps.Add(j);
            var here = SceneManager.CurrentFloorInfo == null ? "" : TextClean.Clean(SceneManager.CurrentFloorInfo.mapName);
            var names = new Dictionary<string, int>();
            foreach (var j in jumps)
            {
                var n = MapData.FloorName(j.mapJumpParam.jumpMapName);
                int c; names.TryGetValue(n, out c); names[n] = c + 1;
            }
            var result = new List<KeyValuePair<MapJump, string>>();
            foreach (var j in jumps)
            {
                var id = j.mapJumpParam.jumpMapName;
                var name = MapData.FloorName(id);
                var label = Strings.Exit(name);
                if (name.Length > 0 && (names[name] > 1 || name == here))
                {
                    var info = MapData.Contents(id);
                    label = Strings.ExitDetail(label, info.People, info.SavePoint);
                }
                result.Add(new KeyValuePair<MapJump, string>(j, label));
            }
            return result;
        }

        /// <summary>L: the closest save point, here or through the exits, and select the way there.</summary>
        public static void FindSavePoint()
        {
            if (!InField()) return;
            var floor = SceneManager.CurrentFloorInfo;
            if (floor == null) return;
            var path = MapData.PathToSavePoint(floor.id);
            if (path == null) { Speech.Say(Strings.NoSavePointFound); return; }
            if (path.Count == 1)
            {
                _category = 4; // save points
                _selected = null;
                Speech.Say(Strings.SavePointHere);
                Cycle(1);
                return;
            }
            // Select the exit that starts the route, so Home and Control Home work on it.
            var next = path[1];
            var player = Player();
            MapJump best = null;
            var bestD = float.MaxValue;
            foreach (var j in Object.FindObjectsOfType<MapJump>())
            {
                if (!j.gameObject.activeInHierarchy || !string.Equals(j.mapJumpParam.jumpMapName, next, System.StringComparison.OrdinalIgnoreCase)) continue;
                var d = player == null ? 0f : (j.transform.position - player.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = j; }
            }
            var route = new List<string>();
            for (var i = 1; i < path.Count; i++) route.Add(MapData.FloorName(path[i]));
            var line = Strings.SavePointRoute(MapData.FloorName(path[path.Count - 1]), route);
            if (best != null)
            {
                _category = 3; // exits
                _selected = best.transform;
                _selectedName = Strings.Exit(MapData.FloorName(next));
                _selectedKind = Kind.Exit;
                line += " " + Describe(_selectedName, best.transform);
            }
            Speech.Say(line);
        }

        private static void Add(List<Target> list, Kind kind, Transform t, string name, Transform player)
        {
            if (t == null || !t.gameObject.activeInHierarchy) return;
            var d = t.position - player.position;
            d.y = 0f;
            // The game parks characters it doesn't need yet far outside the map (seen: 14 km).
            if (d.magnitude > MaxRange) return;
            list.Add(new Target { Kind = kind, Transform = t, Name = name, Distance = d.magnitude });
        }
    }
}
