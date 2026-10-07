using System.Collections;
using System.Collections.Generic;
using System.Text;
using Setsuna;
using UnityEngine;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// ATB battles (UiBattleWindow). Whose turn it is with HP/MP, the command cursor, targets
    /// with HP and status, the skill/item grid with cost and description, damage pop-ups,
    /// skill names, Momentum and Singing Stone (Setsuna system) cues.
    /// BattleCharaList index == player index == UI status index, as the game itself uses it.
    /// </summary>
    internal static class Battle
    {
        private static int _announcedPlayer = -1;
        private static readonly Dictionary<int, int> _stock = new Dictionary<int, int>();
        private static readonly List<string> _hud = new List<string>();
        private static bool _pendingStart;

        // ---- lifecycle ------------------------------------------------------------------

        public static void OnInit(bool isFirst)
        {
            _announcedPlayer = -1;
            _stock.Clear();
            _hud.Clear();
            if (!isFirst) _pendingStart = true;
        }

        public static void LateTick()
        {
            if (_pendingStart && BattleManager.NowBattleState == BATTLE_STATE.ACTIVE_TIME)
            {
                _pendingStart = false;
                var names = new List<string>();
                foreach (var c in BattleManager.BattleCharaList)
                    if (c != null && c.IsLiveEnemy) names.Add(Name(c));
                Speech.Say(Strings.BattleStart(string.Join(", ", names.ToArray())), false);
            }
            if (_hud.Count > 0)
            {
                Speech.Say(string.Join(", ", _hud.ToArray()), false);
                _hud.Clear();
            }
        }

        // ---- commands -------------------------------------------------------------------

        public static void OnCommandCursor(UiBattleWindow win)
        {
            var player = Reflect.Int(win, "nowPlayerIndex");
            var command = Reflect.Int(win, "nowCommandIndex");
            var panel = Reflect.Get<UiBattlePlayerCommand>(win, "uiBattlePlayerCommand");
            if (panel == null || player < 0 || !panel.IsActive(player)) return;

            var labels = new List<string>();
            var list = Reflect.Get<IList>(panel, "battleCommandList");
            if (list != null)
                foreach (var cmd in list)
                    if (Reflect.Get<bool>(cmd, "enable")) labels.Add(Ui.Read(Reflect.Get<Text>(cmd, "text")));
            var label = command >= 0 && command < labels.Count ? labels[command] : "";
            var line = Strings.Item(label, command, labels.Count);

            if (player != _announcedPlayer)
            {
                _announcedPlayer = player;
                var c = Chara(player);
                if (c != null) line = Name(c) + ", " + Vitals(c) + ". " + line;
            }
            Speech.Say(line);
        }

        public static void OnSetActiveCommand(UiBattleWindow win, int playerIndex, bool active)
        {
            if (!active)
            {
                if (playerIndex == _announcedPlayer) _announcedPlayer = -1;
                return;
            }
            // Someone else became ready while another character's menu is open.
            var now = Reflect.Int(win, "nowPlayerIndex");
            if (now >= 0 && now != playerIndex)
            {
                var c = Chara(playerIndex);
                if (c != null) Speech.Say(Strings.Ready(Name(c)), false);
            }
        }

        // ---- targets --------------------------------------------------------------------

        public static void OnTargets(List<int> targets)
        {
            if (targets == null || targets.Count == 0) return;
            if (targets.Count == 1)
            {
                var c = Chara(targets[0]);
                if (c == null) return;
                var line = Name(c) + ", " + Vitals(c);
                var st = Statuses(c);
                if (st.Length > 0) line += ", " + st;
                Speech.Say(line);
                return;
            }
            var names = new List<string>();
            foreach (var i in targets)
            {
                var c = Chara(i);
                if (c != null) names.Add(Name(c));
            }
            Speech.Say(string.Join(", ", names.ToArray()));
        }

        // ---- skill / item grid ----------------------------------------------------------

        public static void OnScrollCursor(UiBattleScrollList list, int x, int y)
        {
            if (Reflect.Int(list, "nowScrollX") != x || Reflect.Int(list, "nowScrollY") != y) return;
            var rows = Reflect.Get<IList>(list, "activeItemList");
            if (rows == null || y >= rows.Count) return;

            int index = 0, count = 0;
            UiBattleScrollItem item = null;
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r] as IList;
                if (row == null) continue;
                if (r < y) index += row.Count;
                if (r == y && x < row.Count) { index += x; item = row[x] as UiBattleScrollItem; }
                count += row.Count;
            }
            if (item == null) return;

            var line = Strings.Item(Ui.ReadAll(item.transform), index, count);
            if (Reflect.Int(list, "listType") == (int)UiBattleScrollList.LIST_TYPE.SKILL)
            {
                var res = Reflect.Get<UiBattleNeedResources>(GuiManager.BattleWindow, "uiBattleNeedRes");
                var cost = res != null && res.gameObject.activeInHierarchy ? Ui.ReadAll(res.transform) : "";
                if (cost.Length > 0) line += ". " + Strings.Cost(cost);
            }
            var desc = TextClean.Clean(item.GetDescription());
            if (desc.Length > 0) line += ". " + desc;
            Speech.Say(line);
        }

        // ---- events ---------------------------------------------------------------------

        public static void OnHud(UiBattleWindow win, Transform target, string value, UiBattleWindow.HUD_TXT_TYPE type)
        {
            var c = target == null ? null : target.GetComponentInParent<BattleCharacter>();
            var name = c == null ? "" : Name(c);
            if (type == UiBattleWindow.HUD_TXT_TYPE.MISS && string.IsNullOrEmpty(value))
                value = Reflect.Get<string>(win, "missStr");
            _hud.Add(Strings.Hud(type.ToString(), name, TextClean.Clean(value)));
        }

        public static void OnSkillName(string text, bool isEnemy)
        {
            var name = TextClean.Clean(text);
            Dialogue.Suppress(text);
            Speech.Say(isEnemy ? Strings.EnemySkill(name) : name, false);
        }

        public static void OnSkillMessage(string text)
        {
            Dialogue.Suppress(text);
            Speech.Say(TextClean.Clean(text), false);
        }

        public static void OnSetsunaGauge(int playerIndex, int stock)
        {
            int old;
            _stock.TryGetValue(playerIndex, out old);
            _stock[playerIndex] = stock;
            if (stock > old)
            {
                var c = Chara(playerIndex);
                if (c != null) Speech.Say(Strings.MomentumCharged(Name(c), stock), false);
            }
        }

        // ---- helpers --------------------------------------------------------------------

        private static BattleCharacter Chara(int index)
        {
            var list = BattleManager.BattleCharaList;
            return list != null && index >= 0 && index < list.Count ? list[index] : null;
        }

        private static string Name(BattleCharacter c) { return TextClean.Clean(c.charaParam.Name); }

        private static string Vitals(BattleCharacter c)
        {
            var p = c.charaParam;
            var s = Strings.Hp(p.Hp, p.MaxHp);
            if (c.IsPlayer) s += ", " + Strings.Mp(p.Mp, p.MaxMp);
            if (!c.IsLive) s += ", " + Strings.KnockedOut;
            return s;
        }

        private static string Statuses(BattleCharacter c)
        {
            var sb = new StringBuilder();
            var st = c.charaParam.charaStatus;
            if (st.buffList != null)
                foreach (var b in st.buffList) Add(sb, b.displayName);
            if (st.debuffList != null)
                foreach (var d in st.debuffList) Add(sb, d.displayName);
            return sb.ToString();
        }

        private static void Add(StringBuilder sb, string s)
        {
            s = TextClean.Clean(s);
            if (s.Length == 0) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(s);
        }
    }
}
