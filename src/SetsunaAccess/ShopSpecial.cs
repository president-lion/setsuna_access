using System.Collections.Generic;
using Setsuna;

namespace SetsunaAccess
{
    /// <summary>
    /// Magic Consortium > Obtain Spritnite (UiShopSpecialWindow). Each spritnite row shows how many can be
    /// obtained now; the materials it needs and how many of each you've sold here are only drawn in a side
    /// panel, so the row gets them read out. Circle opens that materials list (state selectItem), which
    /// has its own cursor (UiCampMateriaMaterial) the generic focus never sees: each material is read with
    /// sold / needed / owned, and Circle there sells it (the shop confirmation is read as for selling).
    /// Triangle on a spritnite row obtains it (OnPressContent; confirmation read by the shop hooks).
    /// </summary>
    internal static class ShopSpecial
    {
        private const int SelectMaterial = 0, SelectItem = 1;
        private static bool _inSetState;

        /// <summary>Row details for a focused spritnite: owned, then each material's progress.</summary>
        public static void OnSelectContent(UiShopSpecialWindow win, ExchangeItemData ex)
        {
            if (Reflect.Int(win, "state") != SelectMaterial) return;
            var parts = new List<string>();
            parts.Add(Strings.Owned(ItemManager.GetHaveItemNum(ex.sellItemId)));
            var mats = Materials(ex);
            if (mats.Count > 0) parts.Add(Strings.Needs(string.Join(", ", mats.ToArray())));
            Focus.Append(string.Join(". ", parts.ToArray()));
        }

        private static List<string> Materials(ExchangeItemData ex)
        {
            var list = new List<string>();
            if (ex.needMaterialIdList == null) return list;
            for (var i = 0; i < ex.needMaterialIdList.Count; i++)
            {
                ItemData item;
                if (!ParameterManager.GetItemData(ex.needMaterialIdList[i], out item) || item == null) continue;
                var sold = Common.GetSellCount(UiShopManager.currentShopId, ex.needMaterialIdList[i]);
                list.Add(Strings.MaterialProgress(TextClean.Clean(item.param.name), (int)sold, ex.needMaterialNumList[i]));
            }
            return list;
        }

        public static void BeforeSetState() { _inSetState = true; }

        /// <summary>Entering the materials list: say what it is, the keys, and the focused material.</summary>
        public static void AfterSetState(UiShopSpecialWindow win, int state)
        {
            _inSetState = false;
            if (state == SelectMaterial)
            {
                // Opening the list or coming back from materials: the keys, after the focused row.
                Speech.Say(Strings.SpritniteKeys(Key(InputManager.InputKey.InputKey_Circle), Key(InputManager.InputKey.InputKey_Triangle)), false);
                return;
            }
            if (state != SelectItem) return;
            Speech.Say(Strings.MaterialsList(Key(InputManager.InputKey.InputKey_Circle)) + " " + Current(win));
        }

        /// <summary>The materials cursor moved (Update_Content calls SetItemData).</summary>
        public static void OnSetItemData(UiShopSpecialWindow win)
        {
            if (_inSetState || Reflect.Int(win, "state") != SelectItem) return;
            Speech.Say(Current(win));
        }

        private static string Current(UiShopSpecialWindow win)
        {
            var panel = Reflect.Get<UiCampMateriaMaterial>(win, "materiaMaterial");
            var ex = Reflect.Get<ExchangeItemData>(win, "currentExchageItem");
            var ids = Reflect.Get<int[]>(panel, "materialItemIds");
            if (panel == null || ids == null || ex.needMaterialIdList == null) return "";
            var index = Reflect.Int(panel, "currentIndex");
            var count = 0;
            foreach (var id in ids) if (id != 0) count++;
            if (index < 0 || index >= ids.Length || ids[index] == 0) return "";
            ItemData item;
            if (!ParameterManager.GetItemData(ids[index], out item) || item == null) return "";
            var need = index < ex.needMaterialNumList.Count ? ex.needMaterialNumList[index] : (short)0;
            var sold = Common.GetSellCount(UiShopManager.currentShopId, ids[index]);
            return Strings.MaterialRow(TextClean.Clean(item.param.name), (int)sold, need,
                                       ItemManager.GetHaveItemNum(ids[index]), index + 1, count);
        }

        private static string Key(InputManager.InputKey k)
        {
            return Strings.KeyName(InputManager.GetKeyBoardSetting(k).ToString());
        }
    }
}
