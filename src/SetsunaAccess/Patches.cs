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

            // Title screen.
            Hook(h, typeof(UiTitleMain), "SetPressObject", postfix: nameof(Title_Press));
            Hook(h, typeof(UiTitleMain), "SetButtons", postfix: nameof(Title_Buttons));
            Hook(h, typeof(UiTitleMain), "Update_Control", postfix: nameof(Title_Move));
        }

        private static void Hook(HarmonyLib.Harmony h, Type type, string method, string prefix = null, string postfix = null)
        {
            try
            {
                var target = AccessTools.Method(type, method);
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
