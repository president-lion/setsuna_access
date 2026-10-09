using System.Collections.Generic;
using Setsuna;

namespace SetsunaAccess
{
    /// <summary>
    /// What a talisman does, for its row in Equip Talisman and in talisman shops. The game only draws it in a side
    /// panel (UiShopAccessoriesCommon.SetAccessories): spritnite slot icons (command / support / either), the
    /// talisman's skill description, and its Flux bonuses (sublimation names). The row itself has only flavour text.
    /// </summary>
    internal static class Talisman
    {
        public static string Describe(UiCampContent row)
        {
            var item = row.CurrentItemData;
            if (item == null || item.type != ITEM_TYPE.ACCESSORY) return null;
            var parts = new List<string>();

            int command = 0, support = 0, either = 0;
            if (item.param.slotTypes != null)
                foreach (var t in item.param.slotTypes)
                {
                    if (t == SLOT_TYPE.SLOT_TYPE_COMMAND) command++;
                    else if (t == SLOT_TYPE.SLOT_TYPE_PASSIVE) support++;
                    else if (t == SLOT_TYPE.SLOT_TYPE_MULTI) either++;
                }
            parts.Add(Strings.TalismanSlots(command, support, either));

            SkillData skill;
            if (ParameterManager.GetSkillData(item.param.skillId, out skill))
            {
                var effect = TextClean.Clean(skill.description);
                if (effect.Length > 0) parts.Add(Strings.TalismanEffect(effect));
            }

            var flux = new List<string>();
            if (item.param.sublimationId != null)
                foreach (var id in item.param.sublimationId)
                {
                    SublimationData s;
                    if (id > 0 && ParameterManager.GetSublimationData(id, out s) && s != null)
                    {
                        var name = TextClean.Clean(s.name);
                        if (name.Length > 0 && !flux.Contains(name)) flux.Add(name);
                    }
                }
            parts.Add(flux.Count > 0 ? Strings.TalismanFlux(string.Join(", ", flux.ToArray())) : Strings.TalismanNoFlux);
            return string.Join(". ", parts.ToArray());
        }
    }
}
