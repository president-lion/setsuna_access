using System;
using HarmonyLib;
using Setsuna;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// Harmony hooks on the game's UI. Applied by hand (no attributes) so one missing method
    /// is logged and skipped instead of stopping the rest.
    /// </summary>
    internal static class Patches
    {
        public static void Apply(HarmonyLib.Harmony h)
        {
            // Every page of dialogue, system and screen text goes through this coroutine.
            Hook(h, typeof(DisplayOneByOneText), "DisplayOneByOneCoroutine", prefix: nameof(Page_Prefix));

            // Dialogue answer balloons (also the common question choices).
            Hook(h, typeof(UiSelectBalloon), "ShowSelectMessage", prefix: nameof(Select_Open));
            Hook(h, typeof(UiSelectBalloon), "SetCursorPosition", postfix: nameof(Select_Cursor));

            // Yes / No system prompts.
            Hook(h, typeof(UiSystemSelectBallon), "Open", prefix: nameof(SysSelect_Open));
            Hook(h, typeof(UiSystemSelectBallon), "UpdateButton", postfix: nameof(SysSelect_Button));

            Hook(h, typeof(UiCommonBalloon), "OpenQuestion", prefix: nameof(Question_Prefix));
            Hook(h, typeof(GuiManager), "ShowFeedMessage", prefix: nameof(Feed_Prefix));

            // Settings screen (shown at boot before the logo, and from the title).
            Hook(h, typeof(UiConfigWindow), "Open", postfix: nameof(Config_Open));
            Hook(h, typeof(UiConfigWindow), "SetItem", postfix: nameof(Config_Row));
            foreach (var m in new[] { "SetLanguage", "SetScreen", "SetReso", "SetController" })
                Hook(h, typeof(UiConfigWindow), m, postfix: nameof(Config_Value));

            // Key Config (from Settings > Control Type).
            Hook(h, typeof(UiConfigHelpWindow), "Open", postfix: nameof(Keys_Open));
            Hook(h, typeof(UiConfigHelpWindow), "SetCursor", postfix: nameof(Keys_Cursor));
            Hook(h, typeof(UiConfigHelpWindow), "SetController", postfix: nameof(Keys_Controller));
            Hook(h, typeof(UiConfigHelpWindow), "KeyAssign", postfix: nameof(Keys_Assign));

            // Generic menu focus: every camp / shop / save / result row and tab.
            Hook(h, typeof(UiChoices), "OnSelect", postfix: nameof(Choice_Select));
            foreach (var t in new[] { typeof(UiCampMenuChoices), typeof(UiCampContent), typeof(UiCampTab),
                                      typeof(UiShopTopButton), typeof(UiCampCharaChoices) })
                Hook(h, t, "SelectLogic", postfix: nameof(Choice_Select));
            Hook(h, typeof(UiCampResidentWindow), "SetMessageBar", prefix: nameof(MessageBar_Prefix));

            Hook(h, typeof(UiSaveLoadWindow), "Open", postfix: nameof(SaveLoad_Open));

            // Shop quantity / price confirmation.
            Hook(h, typeof(UiShopConfirmation), "Open", new[] { typeof(int), typeof(int) }, postfix: nameof(ShopConfirm_Open));
            Hook(h, typeof(UiShopConfirmation), "Update_Number", postfix: nameof(ShopConfirm_Number));

            // Battle results.
            Hook(h, typeof(UiResultWindow), "Open", prefix: nameof(Result_Open));

            // Tutorials and map pictures.
            Hook(h, typeof(GuiManager), "ShowMap", prefix: nameof(Tutorial_Prefix));

            // Naming screens.
            Hook(h, typeof(UiNameBox), "Open", postfix: nameof(Name_Open));
            Hook(h, typeof(UiNameBox), "ButtonUpdate", postfix: nameof(Name_Button));

            // Walk-to-object steering.
            Hook(h, typeof(InputManager), "Update", postfix: nameof(Input_After));

            // Field: place banners and arrival.
            Hook(h, typeof(GuiManager), "OpenTelop", prefix: nameof(Telop_Prefix));
            Hook(h, typeof(GuiManager), "OpenTelopWorld", prefix: nameof(Telop_Prefix));
            Hook(h, typeof(SceneManager), "CameraSetting", postfix: nameof(Floor_Ready));

            // Battle.
            var bw = typeof(UiBattleWindow);
            Hook(h, bw, "InitializeCoroutine", prefix: nameof(Battle_Init));
            Hook(h, bw, "SetCursorPosOnCommand", postfix: nameof(Battle_Command));
            Hook(h, bw, "SetActiveCommand", postfix: nameof(Battle_SetActive));
            Hook(h, bw, "SetCursorPosOnTarget", postfix: nameof(Battle_Targets));
            Hook(h, bw, "ShowHudText", prefix: nameof(Battle_Hud));
            Hook(h, bw, "ShowSkillName", prefix: nameof(Battle_SkillName));
            Hook(h, bw, "ShowSkillMessage", prefix: nameof(Battle_SkillMessage));
            Hook(h, bw, "ShowSetsunaSysAnnounce", prefix: nameof(Battle_SetsunaAnnounce));
            Hook(h, bw, "StartSetsunaSystemInput", postfix: nameof(Battle_MomentumWindow));
            Hook(h, bw, "ShowAutoSetsunaSystemMessage", postfix: nameof(Battle_AutoSetsuna));
            Hook(h, bw, "UpdateSetsunaGauge", postfix: nameof(Battle_Gauge));
            Hook(h, typeof(UiBattleScrollList), "SetCursorPosOnScrollList", postfix: nameof(Battle_Scroll));

            // Title screen.
            Hook(h, typeof(UiTitleMain), "SetPressObject", postfix: nameof(Title_Press));
            Hook(h, typeof(UiTitleMain), "SetButtons", postfix: nameof(Title_Buttons));
            Hook(h, typeof(UiTitleMain), "Update_Control", postfix: nameof(Title_Move));
        }

        private static void Hook(HarmonyLib.Harmony h, Type type, string method, string prefix = null, string postfix = null)
        {
            Hook(h, type, method, null, prefix, postfix);
        }

        private static void Hook(HarmonyLib.Harmony h, Type type, string method, Type[] args, string prefix = null, string postfix = null)
        {
            try
            {
                var target = AccessTools.Method(type, method, args);
                if (target == null) { Log.Error("Patch", "not found: " + type.Name + "." + method); return; }
                h.Patch(target,
                    prefix: prefix == null ? null : new HarmonyMethod(typeof(Patches), prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(typeof(Patches), postfix));
            }
            catch (Exception ex) { Log.Error("Patch", type.Name + "." + method + ": " + ex); }
        }

        private static void Guard(string key, Action a)
        {
            try { a(); } catch (Exception ex) { Log.Once(key, ex); }
        }

        // ---- dialogue -------------------------------------------------------------------

        private static void Page_Prefix(DisplayOneByOneText __instance, string fullText)
        {
            Guard("Page", () => Dialogue.OnPage(__instance, fullText));
        }

        // ---- answer balloons ------------------------------------------------------------

        private static bool _selectOpening;

        private static void Select_Open() { _selectOpening = true; }

        private static void Select_Cursor(UiSelectBalloon __instance, int selectIndex)
        {
            Guard("Select", () =>
            {
                var items = Reflect.Arr(__instance, "selectItemArr");
                if (items == null) return;
                var count = 0;
                string label = null;
                for (var i = 0; i < items.Length; i++)
                {
                    var root = Reflect.Get<UnityEngine.RectTransform>(items.GetValue(i), "transRoot");
                    if (root == null || !root.gameObject.activeSelf) continue;
                    if (i == selectIndex) label = Reflect.Get<Text>(items.GetValue(i), "txtAnswer")?.text;
                    count++;
                }
                var interrupt = !_selectOpening;
                _selectOpening = false;
                Speech.Say(Strings.Item(TextClean.Clean(label), selectIndex, count), interrupt);
            });
        }

        // ---- yes / no prompts -----------------------------------------------------------

        private static bool _sysOpening;

        private static void SysSelect_Open(string _text)
        {
            Guard("SysSelect.Open", () =>
            {
                Speech.Say(TextClean.Clean(_text));
                _sysOpening = true;
            });
        }

        private static void SysSelect_Button(UiSystemSelectBallon __instance)
        {
            Guard("SysSelect.Button", () =>
            {
                var buttons = Reflect.Arr(__instance, "buttons");
                var index = Reflect.Get<int>(__instance, "buttonIndex");
                if (buttons == null || index < 0 || index >= buttons.Length) return;
                var count = 0;
                for (var i = 0; i < buttons.Length; i++)
                {
                    var go = Reflect.Get<UnityEngine.GameObject>(buttons.GetValue(i), "gameObejct");
                    if (go != null && go.activeSelf) count++;
                }
                var label = Reflect.Get<Text>(buttons.GetValue(index), "text")?.text;
                Speech.Say(Strings.Item(TextClean.Clean(label), index, count), !_sysOpening);
                _sysOpening = false;
            });
        }

        private static void Question_Prefix(string str)
        {
            Guard("Question", () => Speech.Say(TextClean.Clean(str)));
        }

        private static void Feed_Prefix(string str)
        {
            Guard("Feed", () => Speech.Say(TextClean.Clean(str), false));
        }

        // ---- settings -------------------------------------------------------------------

        private static void Config_Open(UiConfigWindow __instance)
        {
            Guard("Config.Open", () => ConfigScreen.OnOpen(__instance));
        }

        // isPlaySE is false for the game's own setup calls, true for player input.
        private static void Config_Row(UiConfigWindow __instance, bool isPlaySE)
        {
            if (isPlaySE) Guard("Config.Row", () => ConfigScreen.OnRow(__instance));
        }

        private static void Config_Value(UiConfigWindow __instance, bool isPlaySE)
        {
            if (isPlaySE) Guard("Config.Value", () => ConfigScreen.OnValue(__instance));
        }

        // ---- key config -----------------------------------------------------------------

        private static void Keys_Open(UiConfigHelpWindow __instance, bool isEdit)
        {
            Guard("Keys.Open", () => KeyConfig.OnOpen(__instance, isEdit));
        }

        private static void Keys_Cursor(UiConfigHelpWindow __instance, bool isPlaySE)
        {
            Guard("Keys.Cursor", () => KeyConfig.OnCursor(__instance, isPlaySE));
        }

        private static void Keys_Controller(UiConfigHelpWindow __instance, bool isPlaySE)
        {
            Guard("Keys.Controller", () => KeyConfig.OnController(__instance, isPlaySE));
        }

        private static void Keys_Assign(UiConfigHelpWindow __instance, bool __result)
        {
            Guard("Keys.Assign", () => KeyConfig.OnAssigned(__instance, __result));
        }

        // ---- generic focus --------------------------------------------------------------

        private static void Choice_Select(UiChoices __instance, bool flag)
        {
            if (flag) Focus.Select(__instance);
        }

        private static void MessageBar_Prefix(string str)
        {
            Focus.MessageBar(str);
        }

        private static void SaveLoad_Open(UiSaveLoadWindow __instance)
        {
            Guard("SaveLoad", () =>
            {
                Speech.Say(Ui.Read(Reflect.Get<Text>(__instance, "caption")));
                if (__instance.DataInfoList.Count == 0) Speech.Say(Strings.NoSaveData, false);
                Focus.QueueNext();
            });
        }

        // ---- shop confirmation ------------------------------------------------------------

        private static int _shopCount = -1;

        private static void ShopConfirm_Open(UiShopConfirmation __instance)
        {
            Guard("Shop.Open", () =>
            {
                _shopCount = Reflect.Int(__instance, "itemCount");
                Speech.Say(Ui.ReadAll(__instance.transform));
            });
        }

        private static void ShopConfirm_Number(UiShopConfirmation __instance)
        {
            Guard("Shop.Number", () =>
            {
                var count = Reflect.Int(__instance, "itemCount");
                if (count == _shopCount) return;
                _shopCount = count;
                var price = Ui.Read(Reflect.Get<Text>(__instance, "price"));
                Speech.Say(price.Length > 0 ? Strings.CountPrice(count, price) : count.ToString());
            });
        }

        // ---- field ----------------------------------------------------------------------

        private static void Result_Open(UiResultWindow __instance) { Guard("Result", () => Results.OnOpen(__instance)); }

        private static void Tutorial_Prefix(string _titleId, string _messageId)
        {
            Guard("Tutorial", () =>
            {
                Speech.Say(GameText(_titleId));
                Speech.Say(GameText(_messageId), false);
            });
        }

        /// <summary>A UI message by id with the game's tags (buttons, names) converted as it shows them.</summary>
        private static string GameText(string id)
        {
            string s;
            if (string.IsNullOrEmpty(id) || !ParameterManager.GetUIMessageData(id, out s) || s == null) return "";
            var tags = new System.Collections.Generic.List<UiTagData>();
            if (UiCommon.CheckTag(s, ref tags)) s = UiCommon.ConversionTag(s, ref tags);
            return TextClean.Clean(s);
        }

        private static void Name_Open(UiNameBox __instance) { Guard("Name.Open", () => NameEntry.OnOpen(__instance)); }
        private static void Name_Button(UiNameBox __instance) { Guard("Name.Button", () => NameEntry.OnButton(__instance)); }

        private static void Input_After(InputManager __instance)
        {
            if (AutoWalk.Active) Guard("AutoWalk", () => AutoWalk.AfterInput(__instance));
        }

        private static void Telop_Prefix(string str) { Guard("Telop", () => Field.OnTelop(str)); }

        private static void Floor_Ready(ref FloorDataInfo data)
        {
            var floor = data;
            Guard("Floor", () => Field.OnFloorReady(floor));
        }

        // ---- battle ---------------------------------------------------------------------

        private static void Battle_Init(bool isFirst) { Guard("Battle.Init", () => Battle.OnInit(isFirst)); }

        private static void Battle_Command(UiBattleWindow __instance)
        {
            Guard("Battle.Command", () => Battle.OnCommandCursor(__instance));
        }

        private static void Battle_SetActive(UiBattleWindow __instance, int playerIndex, bool active)
        {
            Guard("Battle.SetActive", () => Battle.OnSetActiveCommand(__instance, playerIndex, active));
        }

        private static void Battle_Targets(System.Collections.Generic.List<int> targetIndexList)
        {
            Guard("Battle.Targets", () => Battle.OnTargets(targetIndexList));
        }

        private static void Battle_Hud(UiBattleWindow __instance, UnityEngine.Transform target, string valueStr,
                                       UiBattleWindow.HUD_TXT_TYPE hudTxtType)
        {
            Guard("Battle.Hud", () => Battle.OnHud(__instance, target, valueStr, hudTxtType));
        }

        private static void Battle_SkillName(string text, bool isEnemy)
        {
            Guard("Battle.SkillName", () => Battle.OnSkillName(text, isEnemy));
        }

        private static void Battle_SkillMessage(string text)
        {
            Guard("Battle.SkillMessage", () => Battle.OnSkillMessage(text));
        }

        private static void Battle_SetsunaAnnounce(string setsunaSysName)
        {
            Guard("Battle.Setsuna", () => Speech.Say(TextClean.Clean(setsunaSysName), false));
        }

        private static void Battle_MomentumWindow() { Guard("Battle.Momentum", Tones.Momentum); }

        private static void Battle_AutoSetsuna(UiBattleWindow __instance)
        {
            Guard("Battle.AutoSetsuna", () =>
            {
                var msg = Reflect.Get<UnityEngine.Component>(__instance, "uiBattleAutoSetsunaMsg");
                if (msg != null) Speech.Say(Ui.ReadAll(msg.transform), false);
            });
        }

        private static void Battle_Gauge(int playerIndex, ref ActiveTimeParam atParam)
        {
            var stock = atParam.setsunaGaugeStock;
            Guard("Battle.Gauge", () => Battle.OnSetsunaGauge(playerIndex, stock));
        }

        private static void Battle_Scroll(UiBattleScrollList __instance, int x, int y)
        {
            Guard("Battle.Scroll", () => Battle.OnScrollCursor(__instance, x, y));
        }

        // ---- title ----------------------------------------------------------------------

        private static void Title_Press(UiTitleMain __instance, bool _active)
        {
            if (!_active) return;
            Guard("Title.Press", () => Speech.Say(TextClean.Clean(Reflect.Get<Text>(__instance, "pressText")?.text)));
        }

        private static void Title_Buttons(UiTitleMain __instance, bool _active)
        {
            if (_active) Guard("Title.Buttons", () => SpeakTitleFocus(__instance, false));
        }

        private static void Title_Move(UiTitleMain __instance)
        {
            Guard("Title.Move", () => SpeakTitleFocus(__instance, true));
        }

        private static void SpeakTitleFocus(UiTitleMain title, bool interrupt)
        {
            var buttons = Reflect.Arr(title, "buttons");
            var index = Reflect.Get<int>(title, "buttonIndex");
            if (buttons == null || index < 0 || index >= buttons.Length) return;
            var label = TextClean.Clean(Reflect.Get<Text>(buttons.GetValue(index), "text")?.text);
            // Button 1 is Load; left/right flips it to Delete (buttonIndex02 == 1).
            if (index == 1 && Reflect.Get<int>(title, "buttonIndex02") == 1) label = Strings.Delete;
            Speech.Say(Strings.Item(label, index, buttons.Length), interrupt);
        }
    }
}
