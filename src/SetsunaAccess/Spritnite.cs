using Setsuna;

namespace SetsunaAccess
{
    /// <summary>
    /// The spritnite list you pick from for a talisman slot (UiCampContent of CampType.Materia, filled by SetMateria).
    /// The game only shows who wears each one as a greyed name; this says who it is equipped on, and when a greyed
    /// row is pressed (PressLogic refuses it silently) why it can't be chosen.
    /// </summary>
    internal static class Spritnite
    {
        public static string Describe(UiCampContent row)
        {
            if (!IsPickList(row)) return null;
            var wearer = Wearer(Reflect.Int(row, "materiaUniqueId"));
            return wearer == null ? null : Strings.EquippedOn(wearer);
        }

        /// <summary>Prefix of UiCampContent.PressLogic: speak why a greyed row can't be picked.</summary>
        public static void OnPress(UiCampContent row)
        {
            if (!IsPickList(row) || row.IsContentSelect) return;
            var reason = Reason(row);
            if (reason != null) Speech.Say(reason);
        }

        private static bool IsPickList(UiCampContent row)
        {
            return row != null && Reflect.Int(row, "type") == (int)CampType.Materia && Reflect.Int(row, "materiaUniqueId") > 0;
        }

        private static string Reason(UiCampContent row)
        {
            var unique = Reflect.Int(row, "materiaUniqueId");
            var item = row.CurrentItemData;
            var win = row.campMateriaWindow;
            var me = win == null ? null : Reflect.Get<CharacterParameter>(win, "currentParameter");
            if (item == null || me == null) return Strings.CantChoose;
            var slots = me.Materia.materiaSlot;
            for (var k = 0; k < slots.Length; k++)
            {
                ItemData other;
                if (!ItemManager.GetItemDataFromUniqueId(slots[k].uniqueItemID, out other)) continue;
                if (slots[k].uniqueItemID == unique)
                    return k == win.CurrentSelectIndex ? Strings.AlreadyInSlot : Strings.EquippedOn(TextClean.Clean(me.Name));
                if (k != win.CurrentSelectIndex && other.param.id == item.param.id)
                    return Strings.AlreadyHasKind(TextClean.Clean(me.Name));
            }
            var wearer = Wearer(unique);
            if (wearer != null) return Strings.EquippedOn(wearer);
            if (item.param.needLevel > me.Level) return Strings.NeedsLevel(item.param.needLevel);
            return Strings.CantChoose;
        }

        /// <summary>Name of the character with this spritnite in a slot, or null.</summary>
        private static string Wearer(int unique)
        {
            if (unique <= 0) return null;
            foreach (var c in ParameterManager.CharacterParameter)
            {
                if (c == null || c.Materia.materiaSlot == null) continue;
                foreach (var s in c.Materia.materiaSlot)
                    if (s.uniqueItemID == unique) return TextClean.Clean(c.Name);
            }
            return null;
        }
    }
}
