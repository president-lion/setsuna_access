using System.Collections.Generic;
using Setsuna;

namespace SetsunaAccess
{
    /// <summary>
    /// Character cards (UiCampCharaChoices) on every camp screen, and the Change Party screen
    /// (UiCampOrganizeWindow): the three battle party slots on the left, the reserve (UiCampResidentWindow) on
    /// the right. Pressing someone chooses them (FieldPressLogic / PartyPressLogic), pressing someone on the other
    /// side swaps the two (Common.SwitchMember); "Add to party" is an empty slot, "Remove from party" takes the
    /// chosen member out of the battle party. Cancel drops the choice (OnPressCross).
    /// </summary>
    internal static class PartyMenu
    {
        private static bool _swapped;

        /// <summary>The whole focus line for a character card, or null for the generic reading.</summary>
        public static string Describe(UiCampCharaChoices card)
        {
            var p = Reflect.Get<CharacterParameter>(card, "parameter");
            var organize = UiCampManager.CurrentCampType == CampType.Organize;
            string line;
            if (p != null)
            {
                var st = p.charaStatus;
                var lv = st.baseStatus.lv;
                line = Strings.CharaCard(TextClean.Clean(p.Name), lv, p.Hp, p.MaxHp, p.Mp, p.MaxMp,
                                         lv >= 99 ? -1 : st.baseStatus.needExp - st.exp);
                var statuses = new List<string>();
                if (st.debuffList != null)
                    foreach (var d in st.debuffList)
                    {
                        var s = TextClean.Clean(d.displayName);
                        if (s.Length > 0) statuses.Add(s);
                    }
                if (statuses.Count > 0) line += ", " + string.Join(", ", statuses.ToArray());
                var role = Reflect.Int(card, "role");
                if (!organize && role >= 0 && role <= 2) line += ", " + Strings.InBattleParty;
                if (organize && st.memberState == MemberState.FIXED) line += ", " + Strings.CantLeaveParty;
            }
            else if (card.IsOutMember)
            {
                line = Ui.ReadAll(card.transform);
                if (line.Length == 0) line = Strings.EmptySlot;
            }
            else return null;

            if (!organize) return line;
            if (Reflect.Get<bool>(card, "isOrganizeSelect")) line += ", " + Strings.Chosen;
            else if (card.IsNotSelect) line += ", " + Strings.Unavailable;
            if (card.campOrganizeWindow != null) return Strings.BattleSlot(card.choicesNumber + 1, 3) + ": " + line;
            if (card.campResidentWindow != null)
            {
                int index, count;
                ReservePosition(card, out index, out count);
                return Strings.Item(Strings.Reserve + ": " + line, index, count);
            }
            return line;
        }

        private static void ReservePosition(UiCampCharaChoices card, out int index, out int count)
        {
            index = 0; count = 0;
            var all = Reflect.Get<UiCampCharaChoices[]>(card.campResidentWindow, "characters");
            if (all == null) return;
            foreach (var c in all)
            {
                if (c == null || !c.IsEnable || !c.gameObject.activeSelf) continue;
                if (c == card) index = count;
                count++;
            }
        }

        /// <summary>Postfix of Common.SwitchMember: say the new battle party.</summary>
        public static void OnSwitch()
        {
            if (UiCampManager.CurrentCampType != CampType.Organize) return;
            _swapped = true;
            Speech.Say(Strings.PartyNow(Names(ParameterManager.FieldMember)));
            Focus.QueueNext();
        }

        /// <summary>Postfix of FieldPressLogic (slot pressed) / PartyPressLogic (reserve pressed).</summary>
        public static void OnPress(UiCampOrganizeWindow win, bool fromSlot)
        {
            if (_swapped) return; // that press made the swap, already said
            var id = Reflect.Int(win, "selectCharaId");
            if (id == 9999) return;
            var who = id == 0 ? (fromSlot ? Strings.EmptySlot : Strings.RemoveFromParty) : Name(id);
            Speech.Say(fromSlot ? Strings.PickReserve(who) : Strings.PickSlot(who));
            Focus.QueueNext();
        }

        /// <summary>Prefix of OnPressCross: a choice dropped by the cancel key (not the reset after a swap).</summary>
        public static void OnCancel(UiCampOrganizeWindow win, bool flag)
        {
            if (_swapped) { _swapped = false; return; }
            if (flag && Reflect.Int(win, "selectCharaId") != 9999) { Speech.Say(Strings.SwapCancelled); Focus.QueueNext(); }
        }

        private static string Names(int[] ids)
        {
            var names = new List<string>();
            if (ids != null) foreach (var id in ids) if (id > 0) names.Add(Name(id));
            return string.Join(", ", names.ToArray());
        }

        private static string Name(int id)
        {
            var chars = ParameterManager.CharacterParameter;
            return chars != null && id > 0 && id <= chars.Count && chars[id - 1] != null ? TextClean.Clean(chars[id - 1].Name) : "?";
        }
    }
}
