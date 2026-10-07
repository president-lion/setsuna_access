# I Am Setsuna — Accessibility Mod: Findings

## Target

| Property | Value |
| --- | --- |
| Game | I Am Setsuna (Steam 441830), `SETSUNA.exe`, version 1.0 |
| Engine | Unity **5.2.2p3**, **Mono**, **x86** |
| `Application.productName` / `companyName` | `SETSUNA` / `TokyoRPGFactory` |
| Code | `Assembly-CSharp.dll` (namespace `Setsuna`, ~517 types) + NLua/KeraLua, decompiled to `research/decomp/` |
| UI | uGUI (`UnityEngine.UI`), managed by `GuiManager` / `UiMessageWindow` / `Ui*` classes |
| Audio | CRI ADX2 (`cri_ware_unity.dll`, `StreamingAssets/x86_64/*.acb` despite the folder name) |
| MelonLoader | **0.5.7** (see below) |

## MelonLoader version — 0.5.7 is the newest that boots

Tested 2026-10-07 by swapping each x86 build in (`tools/try_ml.sh <tag>`):

| Version | Result |
| --- | --- |
| 0.7.3 | Starts, then `Failed to initialize MelonLoader`: `Tomlet.TomlSerializationMethods..cctor` → `TypeLoadException: Could not load type 'Typespec 0x1b000001'` (Unity 5.2's old Mono can't resolve a generic typespec in Tomlet). No mods load. |
| 0.7.2, 0.7.0, 0.6.1 | Game runs, MelonLoader never initialises (empty / no log). |
| 0.6.5, 0.6.0 | Game crashes at start (crash dumps in `research/ml_crashes/`). |
| **0.5.7** | **Boots, loads `Mods\SetsunaAccess.dll`, Harmony available.** |

0.5.7 API differences from the Sally Face mod: `MelonUtils.UserDataDirectory` / `UserLibsDirectory`
(not `MelonEnvironment`), assemblies at `MelonLoader\MelonLoader.dll` and `MelonLoader\0Harmony.dll`
(no `net35\` subfolder). `OnSceneWasLoaded` does **not** fire on Unity 5.2 (`SceneManager.sceneLoaded`
is 5.4+) — scene changes must be polled or hooked in the game's own `Setsuna.SceneManager`.

## Speech

Prism (same commit as Sally Face, 94b62ec) built x86 by `src/build_prism_x86.bat` →
`src/prism/build-x86/prism.dll`, deployed to `UserLibs\`. `Speech.Preload` LoadLibrary's it by full
path so `DllImport("prism")` binds without DLL search paths. All imports are cdecl. Verified in-game:
backend NVDA, "Setsuna Access loaded." spoken and in `UserData\SetsunaAccess\speech.log`.

## Saves

- Slots: `%APPDATA%\Steam\CODEX\441830\remote\setsunaNN.dat` (Steam RemoteStorage via the CODEX emulator,
  `SaveDataManager` → `SteamManager.WriteFile`).
- Config: `Documents\my games\Setsuna\config.dat`.
- Resolution prefs in `HKCU\Software\TokyoRPGFactory\SETSUNA`; `GameManager.Update` rewrites them to
  1280x720 windowed itself whenever the screen mode changes.
- As of 2026-10-07 no save slots exist yet.

## Events are Lua, but every effect is C#

`StreamingAssets/data/event/**/*.lua.out` are Lua 5.2 bytecode. `EventControl.LuaRegisterFunction`
registers public `EventControl` methods into NLua by reflection, so scripts show text, choices and
shops by calling C#. Hooking the C# side catches everything without touching Lua.

## Hook points (first pass)

- Dialogue: `UiMessageWindow.ShowMessage(int id, int se)`, `ShowSystemMessage`, `ShowOneMessage`,
  `ShowScreenMessage`, text via `UiMessageWindow.GetMessage(ushort id)`; speaker lookups in `UiNameBox`.
- Choices: `UiMessageWindow.ShowSelectMessage` / `ShowCommonSelectMessage`, `OpenSystemSelect`.
- Generic menu items: `UiChoices.OnSelect(bool, bool)` — every selectable widget goes through it.
- Battle: `BattleManager`, `UiBattlePlayerCommand`, `UiBattleSetsunaSysAnnounce.Show(name)`, `UiBattleScrollList`.
- Camp menu `UiCamp*`, shops `UiShop*`, saves `UiSaveLoadWindow`, feed messages `GuiManager.ShowFeedMessage`.

## Screens and input (from code)

- Boot order: `BootManager` opens the **Settings** window (`UiConfigWindow`) first, before the logo and
  title. The level stays `Boot` until the player leaves it with Exit Settings.
- Settings rows (`ITEM`): Language, Screen Mode, Resolution, Control Type, Exit Settings. Up/down picks
  a row, left/right changes it, Confirm on Control Type opens Key Config (`UiConfigHelpWindow`, not yet read).
- Keyboard defaults (`InputManager.ResetKeyBoardSetting`): Confirm = Return or Space, Cancel = K,
  move = W/A/S/D (arrow keys also feed Unity's Horizontal/Vertical axes), menu = I, Momentum = Left Ctrl.
- Title (`UiTitleMain`): Up *increments* the index; 0 = New Game, 1 = Load Game; left/right on Load flips
  to Delete (`buttonIndex02`). The first key on "Press Any Button" also counts as a move.
- Mod keys: F3 repeat, F11 UI dump. **F12 belongs to the user's NVDA** (speech history).
