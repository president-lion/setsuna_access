using System.IO;
using Setsuna;

namespace SetsunaAccess
{
    /// <summary>
    /// One-shot: if UserData\SetsunaAccess\reset_keys exists when the title screen appears, restore the
    /// game's default keyboard and pad bindings (the same calls as Key Config's Restore Defaults) and
    /// save the config, then delete the request file. For when Key Config got rebound by accident.
    /// </summary>
    internal static class KeyReset
    {
        public static void RunIfRequested()
        {
            var flag = Path.Combine(Log.Dir, "reset_keys");
            if (!File.Exists(flag)) return;
            File.Delete(flag);
            InputManager.ResetKeyBoardSetting();
            InputManager.ResetPadSetting();
            GuiManager.UpdateButtonIcon();
            GameManager.SaveConfig();
            Log.Info("KeyReset", "keyboard and pad bindings restored to defaults and saved");
            Speech.Say(Strings.KeysReset);
        }
    }
}
