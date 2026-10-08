using System.Collections.Generic;
using Setsuna;
using UnityEngine;

namespace SetsunaAccess
{
    /// <summary>
    /// Camp > Weapons (UiCampWeaponWindow). Confirming a weapon equips it at once with no menu, and the
    /// comparison against the equipped weapon is only drawn (blue up / red down), so the mod says it:
    /// stats computed exactly as UiCampWeaponCommon.SetSelectWeapon does (base + upgrades).
    /// Triangle opens the upgrade panel (UpgradeWeapon state), which is read too.
    /// </summary>
    internal static class WeaponMenu
    {
        /// <summary>For a weapon row: "equipped", or what changes compared with the equipped weapon.</summary>
        public static string Compare(UiCampContent row)
        {
            if (Reflect.Int(row, "type") != (int)CampType.Weapon) return null; // not upgrade materials
            var win = row.campWeaponWindow; // the panels live under a separate UI root, not under the window
            if (win == null) return null;
            var chara = Reflect.Get<CharacterParameter>(win, "currentParameter");
            if (chara == null || row.uniqueId == 0) return null;
            if (row.uniqueId == chara.EquipWeaponUniqueItemID) return Strings.EquippedNow;

            short[] cur, sel;
            if (!Stats(chara.EquipWeaponUniqueItemID, out cur) || !Stats(row.uniqueId, out sel)) return null;
            var parts = new List<string>();
            for (var i = 0; i < 4; i++)
                if (cur[i] != sel[i]) parts.Add(Strings.StatChange(i, cur[i], sel[i]));
            return parts.Count == 0 ? Strings.SameStats : string.Join(", ", parts.ToArray());
        }

        /// <summary>Physical attack, physical defense, magic attack, magic defense, including upgrades.</summary>
        private static bool Stats(short uniqueId, out short[] stats)
        {
            stats = new short[4];
            ItemData item;
            if (!ItemManager.GetItemDataFromUniqueId(uniqueId, out item) || item == null) return false;
            var info = ItemManager.GetItemInfoFromUniqueId(uniqueId);
            stats[0] = (short)(item.param.physicalAttack + (info == null ? 0 : info.UpPhysicalAttack));
            stats[1] = (short)(item.param.physicalDefense + (info == null ? 0 : info.UpPhysicalDefense));
            stats[2] = (short)(item.param.magicAttack + (info == null ? 0 : info.UpMagicAttack));
            stats[3] = (short)(item.param.magicDefense + (info == null ? 0 : info.UpMagicDefense));
            return true;
        }

        public static void OnEquip(UiCampWeaponWindow win, HaveItemInfo info)
        {
            ItemData item;
            if (info == null || !ItemManager.GetItemDataFromUniqueId(info.uniqueId, out item) || item == null) return;
            Speech.Say(Strings.Equipped(TextClean.Clean(item.param.name)));
        }

        /// <summary>Triangle: the upgrade panel opened.</summary>
        public static void OnUpgradeOpen(UiCampWeaponWindow win)
        {
            if (Reflect.Int(win, "state") != 1) return; // UpgradeWeapon
            var panel = Reflect.Get<Component>(win, "upgradeWeaponWindow");
            if (panel != null) Speech.Say(Strings.UpgradeTitle + " " + Ui.ReadAll(panel.transform));
            Focus.QueueNext();
        }

        /// <summary>An upgrade material is focused: the panel now shows the weapon before and after.</summary>
        public static void OnUpgradePreview(UiCampWeaponWindow win, ItemData material)
        {
            if (material == null) return;
            var panel = Reflect.Get<Component>(win, "upgradeWeaponWindow");
            if (panel != null) Focus.Append(Ui.ReadAll(panel.transform));
        }

        public static void OnUpgraded(UiCampWeaponWindow win)
        {
            var current = Reflect.Get<Component>(win, "currentWeaponData");
            Speech.Say(Strings.Upgraded + (current != null ? " " + Ui.ReadAll(current.transform) : ""));
        }
    }
}
