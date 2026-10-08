using Setsuna;
using UnityEngine.UI;

namespace SetsunaAccess
{
    /// <summary>
    /// After a battle: "Please select Fluxes to add" (UiResultWindow, ChackSublimation state). Each row is a
    /// spritnite with one new Flux and an Off / On pair (UiCampContent.button_cursor, buttonIndex 1 = On, the
    /// default). Left / right call MoveButton, which tells the window via OnSublimationSelect; the cancel button
    /// confirms and adds every Flux left On. The row only showed both labels, so the mod says the setting.
    /// </summary>
    internal static class FluxMenu
    {
        /// <summary>For a Flux row: its current setting; null for any other row.</summary>
        public static string State(UiCampContent row)
        {
            if (Reflect.Get<UiResultWindow>(row, "resultWindow") == null) return null;
            var buttons = Reflect.Get<Image[]>(row, "button_cursor");
            if (buttons == null || buttons.Length == 0) return null;
            return Strings.FluxSetting(row.GetButtonEnable);
        }

        /// <summary>Postfix of UiCampContent.MoveButton (left / right on a Flux row).</summary>
        public static void OnMove(UiCampContent row)
        {
            if (Reflect.Get<UiResultWindow>(row, "resultWindow") == null) return;
            Speech.Say(row.GetButtonEnable ? Strings.On : Strings.Off);
        }

        /// <summary>Postfix of UiResultWindow.OpenChackSublimation: how the screen works.</summary>
        public static void OnOpen(bool open)
        {
            if (!open) return;
            Speech.Say(Strings.FluxHelp(Strings.KeyName(InputManager.GetKeyBoardSetting(InputManager.InputKey.InputKey_Cross).ToString())), false);
        }
    }
}
