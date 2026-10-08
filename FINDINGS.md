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
- Keyboard defaults (`InputManager.ResetKeyBoardSetting`): Confirm = Return or slot 1 (Space); move =
  W/A/S/D (slots 9-12, read directly in `InputManager`). Other slots, by the pad names at the same index:
  2 Cross = K, 3 Square = H, 4 Triangle = J, 5 RightShoulder = I, 6 LeftShoulder = U, 7 Start = 1,
  8 Select = Left Ctrl, 13 = M, 14 = N, 15 = Delete. Which action each drives is still to be read.
- Title (`UiTitleMain`): Up *increments* the index; 0 = New Game, 1 = Load Game; left/right on Load flips
  to Delete (`buttonIndex02`). The first key on "Press Any Button" also counts as a move.
- Mod keys (Stardew Access layout, user's request): Z location, PageUp/PageDown object, Ctrl+PageUp/PageDown
  category, Home object info, Ctrl+Home walk to object, End beacon, F1 help, F3 repeat, P party HP/MP, F11 dump.
  **F12 belongs to the user's NVDA** (speech history). Left Ctrl is the game's "Select", used only on the Key
  Config help screen (`UiConfigHelpWindow.UpdateInfo`), so Ctrl combos are safe elsewhere.
- Player movement: `BaseObject.CreateMoveVec` = camera-right * Horizontal + cross(camera-right, up) * Vertical,
  from `InputManager.horizontal/vertical`, computed in `InputManager.Update`. Walk-to overrides them in a postfix.
- `KeyboardManager` is a console stub; naming uses `UiNameBox` (a real InputField).
- Tutorials: Lua `ShowTutorial` -> `GuiManager.ShowMap(type, sprite, titleId, messageId)`.
- Battle results: `UiResultWindow.state` (ExpEnd / ShowGetItem / ChackSublimation wait for input).

## What the mod hooks (current)

| Area | Hook | Notes |
| --- | --- | --- |
| Dialogue / system text | `DisplayOneByOneText.DisplayOneByOneCoroutine` | Speaker from the balloon's `txtName`; non-balloon text queues |
| Answers, Yes/No | `UiSelectBalloon.SetCursorPosition`, `UiSystemSelectBallon.Open/UpdateButton` | |
| Settings | `UiConfigWindow.Open/SetItem/Set*` | Option buttons have no Text: language and control labels from `text[]`, screen mode from `txt_full`/`txt_win`, resolution from `resoText[]` |
| Key Config | `UiConfigHelpWindow.Open/SetCursor/SetController/KeyAssign` | In edit mode **any A-Z/0-9/Space press assigns** to the focused row (`KeyAssign` runs before navigation) |
| Menus (camp, shop, save, result) | `UiChoices.OnSelect` + every `SelectLogic` | Spoken in LateUpdate: row text, position from `UiCampContentController` (`CurrentContentNo`/`contentMaxNum`), description from the row's `ItemData`/`SkillData`, camp message bar |
| Save / Load | `UiSaveLoadWindow.Open` | caption, "No save data" |
| Shop quantity | `UiShopConfirmation.Open/Update_Number` | |
| Battle | `UiBattleWindow.*`, `UiBattleScrollList.SetCursorPosOnScrollList` | turn + HP/MP, commands, targets with HP + buff/debuff names, skill grid with cost and description, HUD numbers batched per frame, skill names, Setsuna-system names, Momentum gauge stocks, 0.5 s Momentum press window = tone |
| Field | `GuiManager.OpenTelop*`, `SceneManager.CameraSetting` | place names; scanner over `NPCControl`, `ItemBox` (`isOn` = opened), `MapJump` (destination via `GetFloorData`), `SavePoint`, `ShiningPoint` (`isPopItem && !isItemGet`) |

**Singletons create themselves on access** (`SingletonMonoBehaviour.Instance` adds a new GameObject when
none exists). Never poll `SceneManager`, `BattleManager`, `FieldPartyManager` from the mod outside the
state where they already exist; the field code checks the level name contains "_" first.

## Notes from play (2026-10-07)

- Escape = `GameManager` -> `GuiManager.OpenQuitWindow` -> `UiSystemSelectOnFrontWindow` ("quit game?"),
  same layout as `UiSystemSelectBallon`. The main menu is "Triangle" (keyboard J by default) in
  `UiCampManager.Update`. Momentum is "Square" (H) in `UiBattleWindow.Update`.
- The game parks NPCs it doesn't need yet far off-map (a "Mysterious Man" at 14 km in the first forest).
  The scanner ignores anything beyond 1 km. `BaseCharacter.IsVisible` is camera-frustum culling, not "hidden".
- **Unity audio is disabled in this build** (`AudioManager.m_DisableAudio = true` in mainData); all game sound
  is CRI. The mod's tones (beacon, Momentum window) play through winmm `PlaySound` with an in-memory WAV.
- Techs / Weapons / Accessories / Spritnite tabs (`UiCampTab`) are face icons with no Text; the tab's
  `parameter` (CharacterParameter) names it. Story tabs use `storyType.ToJapanese()` (localized despite the name).
- One Page Down showed up as two key-downs ~70 ms apart in play; hotkeys are debounced 150 ms.

## Event scripts and narration

- `research/events/` (not in git): all 392 Lua 5.2 scripts decompiled with unluac
  (`tools/dl/unluac_src`, built with javac; JDK 21 is installed). Character ids are constants in
  `CommonModule.lua` (e.g. `KITO = "NPC_10020"`, the first forest's Mysterious Man).
- Lua helpers wrap C#: `playAnimation` -> `EventControl.CrossFadeAnimation`, `playEmotion(Wait)` ->
  `PlayEffectToPosition(EMO_*)` above the head, `moveCharaToPosXZ` -> `MoveCharaToXZ`, `fadeInBlack` -> `FadeIn`,
  `mes(n)` -> `ShowMessage`.
- Narration hooks those: gestures by animation name, emotion bubbles by effect id (nearest character),
  EnableCharacter / FadeCharacter appear/vanish (only on a visible screen), walking up to someone or away,
  camera shake, and "Nearby: ..." when an event fades in from black. Buffered and prefixed to the next
  dialogue page. V toggles it.
- Camp Settings rows (`UiCampConfigChoice`): value from `buttonIndex` (label = nearest Text to the highlighted
  option image) or slider `value`; names from `UiConfigCategory.ToName/ToAbout`.

## Map data from parameter.cpk

- `parameter.cpk` (CRI, plain @UTF tables) holds every parameter file; `tools/cpk.py` extracts them
  (`research/params/`, not in git) and `Cpk.cs` reads them in the mod.
- All parameter files except EncryptionData, BraveStoryMessage and Parameter are AES (RijndaelManaged
  defaults) with key `8xTD|EgD|b?07QDj`, IV `/]s@*CxLzM!9Qd%(` (`ParameterManager.DecryptParameter`).
- `<sceneName>Placement` = a floor's objects. `ObjectPlacementManager.CreatePlacementParameter(ref byte[],
  ref PlacementParameter)` is pure, so the mod parses other floors with the game's own code. Group
  visibility = `IsPlacement` logic (invalidFlg, min/maxProgress, enableFlg), reimplemented side-effect free.
- Floors have no sub-names: house interiors carry their village's `mapName`. Exit labels add the
  destination's people / save point when names repeat. `L` = breadth-first search over exits for the
  closest floor with a placed save point, selecting the first exit.
- **Buildings have no names in the data.** House interiors reuse the village's FloorDataMessage entry,
  Snow Chronicles geography has no per-floor names, and no `CommonSign` text names a building (signs read
  `ScenarioMessageData_NormalConversation` by the EventCollision id; none mention shops or houses).
  What identifies a building is who works there: shopkeepers' placement `common.script` is
  `ShopNpc_Item/Magic/Cooking/Accessory`, named with the shop's own title (UI messages SHOP01/00/02/03).

## Route guidance (no navmesh)

- No baked navmesh (`NavMeshLayerName.names` is empty). Characters: rigidbody + capsule, ground snapped by
  a downward ray on `HitGround` from 1.5 m up (`BaseCharacter.UpdateHeight`); a drop over 0.5 m makes them
  fall (`OnFall`). Walls are `HitWall` (`BaseObject.wallLayerMask` = HitGround|HitWall); the player also
  avoids NPC capsules (`PlayerControl.CheckNpcCollision`).
- `Nav` probes 0.5 m cells lazily: ground ray on HitGround (rise <= 0.5, drop <= 0.45 per cell) and
  `Physics.CheckCapsule` (party collider size) against HitWall|NPC|Enemy, triggers ignored. `GridPath` = A*
  (8-way, no corner cutting, 12000 expansions). `Guide` re-plans every 2.5 s or when 2.5 m off the route,
  aims at the farthest straight-line-clear point (SphereCast + ground continuity), retries with a looser goal
  for exits past the walkable edge. Bumps and walk-to stalls mark the cell ahead blocked for that scene.
- Each plan is logged to `UserData\SetsunaAccess
av.log`.
- Scanner filter: one `GridPath.Flood` from the player (40000 cells, cached 5 s / 2 m) over the same grid;
  objects need a reached cell within 1.2-2.5 m (people 2.2 for counters, exits 2.5). Beyond the fill = kept.
  Shift+End toggles. Bumps mark a 3-cell strip and log the solid colliders hit; a layer not yet blocking
  (and not the floor underfoot) is added to the block mask for the session.
- World map = scene `ma_0000_01` (`Common.SCENE_NAME_WORLD_MAP`); save allowed there up to progress 421010,
  so L treats it as a save spot. Walk-to cancels only on a held movement key (bound keys or arrows): in play
  it was stopping itself within a second.
- In play the user's Key Config got rebound by accident (any key press assigns in edit mode).
- **Walls are mostly on HitGround** (bump log: `MergedCollider` / `HitGround` objects, layer HitGround).
  A downward ray starting inside a tall rock face misses it, so clearance also checks HitGround with a
  capsule (and straight-line SphereCast) raised 0.6 m above the cell floor.
- Scanner/beacon lag: the reachability flood now runs as `GridPath.FloodJob` slices (2 ms/frame);
  clearance results kept 8 s; re-plan every 4 s; route budgets 8000 / retry 3000.
- The full-height HitGround capsule was too strict (flood boxed in at ~30 m in Dazzshire Woods, hiding
  reachable chests). Replaced with a knee-height (0.6 m) `Physics.Linecast` between neighbouring cells, for
  routes only; the scanner filter floods with lenient rules (no rock-face line) so it never over-hides.
- perf.log in play: mod 0.01-0.05 ms/frame, 60 fps. High CPU is the game, not the mod.
- Frame cap (F2, MelonPreferences `SetsunaAccess.FrameCap`, default 30): vSyncCount 0 + targetFrameRate,
  re-applied every frame because `UiBattleWindow.OnDecideSetsunaSystem` sets targetFrameRate = 10 and never
  restores it (harmless only while vsync is on). 0 restores the game's vsync.
- Slope limits per 0.5 m cell are now rise 0.8 / drop 1.0 (the game only drops you for >0.5 m in one
  frame of movement and pushes up slopes physically); 0.5/0.45 boxed the woods' hills in. Each finished
  flood logs why steps were refused (no ground / rise / drop / blocked / learned).
- Route planning also runs in slices (`GridPath.SearchJob`, 2 ms/frame via `Guide.Tick`); the old route
  stays in use meanwhile. Home finishes a pending plan immediately (`Guide.Complete`).
- Layer collision matrix (mainData PhysicsManager): Player collides with Default, Enemy, NPC, Object,
  HitCollision, HitGround, HitWall (and render layers). Layers: 8 Player, 18 Enemy, 19 NPC, 20 Object,
  21 HitCollision, 22 HitGround, 23 HitWall.
- Dazzshire Woods flood log: every refused step was "blocked" (body capsule vs HitWall|NPC|Enemy), none for
  slope or ground. The reachability flood now tests a 12 cm capsule and logs the top blocking colliders;
  routes test at most 0.22 m radius.

Serendale (ma_0009_01), 2026-10-07: the body-width plan, the loose goal and the slim body all exhausted a closed
area of about 2600 cells around the town centre, while the lenient reachability flood reached the save point.
So a fourth fallback plans with the flood's own rules (ground, slope, 12 cm capsule; no rock-face rays or knee
line) and a strong wall cost (+4 within the body radius, +1.5 within radius + 0.45 m). Re-plans start from the
mode that last worked. Failed searches now log refusal counts (learned, no ground, rise, drop, body, rock face)
to show which test closes an area off. Walk-to gives up after 15 s without getting 1 m closer. Home waits at
most 30 ms for a route, then says "Finding the way" and speaks the route when the search finishes.
perf.log names the slowest part of the mod's frame per 10 s window (and the key, for key presses).
Follow-up: the lenient plan walked into Serendale's ground-layer barriers (wall1, pCube1/3/4 boxes near
(28,22) during the monster attack): the 12 cm capsule only tests HitWall/NPC/Enemy. Both the scanner flood and
the lenient plan now refuse a step when a knee-height line between the cells (cast both ways; mesh faces are
one-sided) meets a ground-layer face with |normal.y| < 0.35. Upward-facing slopes and bumps don't count.
Walk-to now stops at once with "No way to X" when every plan fails on the first search (or twice in a row later).

## Story objective (N)

- `GameFlow` (AES like other params) = 278 entries of `GameFlowInfo` (Pack 2, 96 bytes + flag strings,
  `dataSize` per entry). Entry applies when `startProgress == EventProgression` (-1 = always).
  `trigger`: 1 talk (terms = character object name, e.g. NPC_22010 / CP_0002), 2 enter trigger zone
  (terms = EventCollision id, EC_...), 3 event end, 4 battle end (terms = enemy group id, EBT_...),
  5 floor in (terms = floor id), 7 battle definitions (battleID set, terms empty).
- N: candidates at the current progress; found in this area (BaseCharacter name / EventCollision id /
  EnemyControl.group.id) -> selected for Home/Ctrl+Home/beacon; else BFS over maps (`MapData.PathTo`)
  for the floor holding it, selecting the first exit. Each lookup is logged to nav.log.
- Party collider radius is 0.5 m in play (`nav.log`). Routes plan with the leader's real `Capsule` radius x0.85;
  clearance adds level ray rings at 0.5 m and 1.0 m against HitGround (rock faces, not overhangs); A* adds a
  cost near walls so routes keep to the middle; fallback plans: looser goal, then slim body (0.22) for doorways.
  Walk-to slides along walls (SphereCast ahead), backs off 0.35 s when stuck, and ignores a stop press in its
  first 1.5 s (held Ctrl+Home repeats were stopping it).

## Weapons menu

- Confirming a weapon (`UiCampWeaponWindow.OnPressContent`) equips it immediately; the comparison panel
  (`UiCampWeaponCommon.SetSelectWeapon`) is colour-only. The mod computes the same base + upgrade stats and
  speaks the changes on each weapon row, "Equipped X" on confirm, the upgrade panel on Triangle
  (`OnPressTriangle`, state UpgradeWeapon), the preview per material (`OnSelectUpgradeItem`) and the result.
- Camp panels are built under separate UI roots (`CreateUiObjFromResCoroutine(..., parentName)`), so use the
  row/tab's own window properties (`campWeaponWindow`, `campSkillWindow`, ...) rather than GetComponentInParent.
- nav.log now logs map changes, selections, route attempts (full / loose goal / slim body, cells searched vs
  budget), walk start / each stall / end with time and distance, bumps with push direction, flood origin.

## Snow Chronicles

- `UiCampStoryWindow`: category tabs (`UiCampTab.storyType`), large list, small list (both `UiCampContent`
  rows read by Focus), detail panel `storyAbout` (`UiCampStoryAbout.OnSelectSmallContent`, synchronous),
  `storyHistory` (records + completion rates, `UpdateHistory`). States: History, LargeContent, SmallContent.
  The mod appends the detail panel text on entry focus, the location panel in the large Location list, and
  the History panel when that state opens.

## Custom scanner names

- Ctrl+F2 renames the selected scanner object via `TextEntry` (Input.inputString; game input blocked by
  clearing InputManager's isInput*/axis/stick fields after its Update and short-circuiting GetButton*/
  GetLeftStick/GetRightStick/GetKeyCodeDown, for one extra frame after Enter).
- Keys: `floorId|Kind|objectName` (+ `|x,z` rounded for things that don't walk). Player file
  `UserData\SetsunaAccess
ames.txt`; built-in defaults in embedded `src/SetsunaAccess/CustomNames.txt`
  (copy players' entries there to ship them).
- Unity's targetFrameRate cap at 30 still left SETSUNA.exe at ~92% CPU. The cap now paces frames itself:
  vSyncCount 0, targetFrameRate -1, then after WaitForEndOfFrame the main thread Thread.Sleeps the rest of the
  frame (timeBeginPeriod(1)). perf.log logs main-thread work per frame and the time slept, to tell a busy
  wait from genuinely heavy frames or other threads.

Objective order (2026-10-08): at progress 121100 GameFlow says "floor in MA_0010_01" (the story battle
EBT_121080 was already won), but N fell back to the nearest field monster before looking for the map, and sent
the party at a stray enemy behind Serendale's barrier. Now: this area's target, then the map path, and the
nearest-enemy guess only when the table has no step at all. Map-path results are logged to nav.log.

## Magic Consortium: Obtain Spritnite (UiShopSpecialWindow)
States: selectMaterial (spritnite list; row number = how many can be obtained now), selectItem (materials
list, own cursor UiCampMateriaMaterial.currentIndex / materialItemIds, moves call SetItemData), confirmation
states use UiShopConfirmation (already read). Circle on a row opens the materials list; Circle there sells the
focused material (OnPressSquare -> Sell confirmation); Triangle on a row obtains (OnPressContent). Progress per
material is Common.GetSellCount(currentShopId, id) against ExchangeItemData.needMaterialNumList. Hooks:
OnSelectContent (row details), SetState (list header and keys), SetItemData (material focus).

House doors (Purikka, 2026-10-08): doors are solid ground-layer colliders (col_mg_door_common_01_01) and the
exit trigger (mjarea_*) sits just behind them, so no walkable cell was within the loose goal (1.7 m) of the
exit point and every plan failed. Exit fallbacks now aim for within 3 m, the scanner counts exits reachable
within 3 m, and the walk's last 3.5 m to an exit go straight in without wall sliding.

Empty shop lists: UiShopBuyWindow.Setup_Blank shows blankWindow with UiShopBuyType.ToNotMessage() (SHOP73 items, SHOP74 cooking, ...). A chef sells nothing until recipes are learned; the mod now speaks that message.

Reachability on the world map (2026-10-08): the 30000-cell flood ran out about 125 m from the player, so far exits (Morthshaw Woods, 346 m) were Unknown and always shown. Flood cap now 400000 cells on the world map (4 ms slices) and 60000 elsewhere. A complete flood stays valid anywhere inside it: it is only redone when the player leaves it, something is learned, or after 60 s. Incomplete floods keep the old 10 s / 3 m refresh.

Softlock in Serendale (2026-10-08): PlayerControl.WalkUpdate refuses every step while a sphere of 0.9 x
capsule radius at the capsule centre overlaps any non-sphere HitGround collider, so a leader that ends up inside
a wall mesh can't move at all. Unstick.cs records the last clear position (every 0.3 s) and, after 2 s of
pushing without moving while overlapping, moves the leader back and calls PlayerControl.ChangeScene() (snaps
to ground, resets beforePosition/air state, as on map entry).
The same session the player ran east along z = 38 from x 17 to 36 with no bump, through what both the planner
and the flood called walled. Nav now records walked cells (TrackWalked); a step between walked cells is always
allowed, and the first 40 walked steps the probes would refuse are logged with the collider and test that
refused them ("walked a step the probes refuse"). Walk-to no longer refuses when no plan works: it says "No
known path. Trying straight." and stops after 8 s without getting closer.

Serendale east side (2026-10-08): offline rasterizing of the scene's two MeshColliders (research/geo/mesh.py,
walk.py; UnityPy) shows the town centre and the east part (save point, objective EC_121180_0 at 32.5,53.5)
separated by a ~6 m strip with no floor. Placement lists gimmicks mg_brge_01 (BridgeControl) and mg_swtc_01
(GimmickSwitch): the way east is a bridge worked by a switch. Both Serendale arrival points (mjpoint_01 south,
mjpoint_02 north) are in the centre, so it isn't a second entrance. The runtime pCube/wall1 HitGround boxes
are part of that crossing. The mod now: lists switches in the scanner ("Switches" category; name says used /
not working yet from GimmickSwitch isOn / isPower), forgets the probed grid when any BaseGimmickObject changes
(isOn, isGimmickPlaying, active), and N, when the objective is cut off, first looks for another entrance
(MapData.Jumps arrivals/exits) and then suggests the nearest unused switch. Walked-cell learning skips steps
whose knee line meets an upright face: the game moves the party by transform and lets it clip into walls.

Scanner coverage (2026-10-08): the placement file holds event colliders, map jumps, gimmicks, save points,
shining points, item boxes, enemies, NPCs, BGM colliders and gimmick cameras. The scanner now covers all the
player-facing ones: people (signboards are NPCs running the CommonSign script, labelled "Sign"), chests,
exits, save points, sparkles, "Switches and doors" (GimmickSwitch, DoorControl with open / locked by key item
gimmickParam.argument1 / opened by switch, AirShipControl), and Enemies (EnemyControl on the field). Event
colliders stay out of the list (N finds the story one); BGM colliders and cameras are not things to walk to.

Flux selection after battle (UiResultWindow, ResultState.ChackSublimation): rows are UiCampContent with
resultWindow set; up/down via sub_contentController, left/right call UiCampContent.MoveButton (buttonIndex 1
= On, default; GetButtonEnable), which calls OnSublimationSelect. Cancel (Cross) applies every row left On
(HaveItemInfo.Sublimate). The mod speaks the row's setting, On/Off on toggle, and a key hint when
OpenChackSublimation(true) runs.

Frost Caves (2026-10-08): the objective EC_121240_0 (-46.5, 3.5, 53) is on room 2's upper ledge (y 3), which
doesn't connect to the floor you arrive on from room 1 (offline flood with tools/geo agrees). Room 2's upper
arrival mjpoint_02 (-47.5, 3, 70) is entered from MA_0011_03's mjarea_01; MA_0011_03 has its own world-map
entrance (mjpoint_11 -> its mjpoint_02). MapData.PathTo now has a variant that only leaves the current floor by
exits the player can reach and only enters a split floor at a given arrival point; Objective remembers that
arrival point so N on the world map and in room 3 keeps routing to the right entrance, and SelectRoute avoids
exits the player can't reach. Failed route chains now retry every 12 s instead of 5 s.
CPU: perf.log gets a "cpu" line every 10 s (process CPU, busiest threads named by the DLL their start address
is in, via Toolhelp32 + GetThreadTimes + NtQueryInformationThread class 9) plus garbage-collection counts.

World map routes (2026-10-08): walking to Frost Caves' far entrance (-95.5, -30), the full-body plan exhausted a
closed area (~4000 cells), then the slim and lenient fallbacks routed through HitWall gaps the party can't pass,
and walk-to bumped along them. On the world map the fallbacks are now off (full + loose goal only), and walk-to
stops with "No way to walk to X from here" instead of trying straight. Room 2's upper ledge drops to the floor
but can't be climbed from it (offline: flood from the ledge reaches the floor, not the reverse).

Ledge drops (2026-10-08): Frost Caves room 2 is a ramp up from the south floor to an upper area, a ~3 m drop
from there to the north floor, and a climb from the north floor to the objective ledge. The game lets you walk
off any ledge (PlayerControl.WalkUpdate moves on with no ground below; UpdateHeight then makes the party fall),
but the mod limited drops to 1 m and probed only 2.5 m down, so every drop read as a wall and everything past
one looked unreachable. The user took this for areas "unlocking" as monsters died. MaxDrop is now 6 m (one-way
steps in the directed flood and A*), and the ground probe reaches 8 m down.

Stacked levels (2026-10-08): the real Frost Caves problem. Room 2's upper walkway runs directly above parts of the
lower floor. The grid had one cell per 0.5 m square, so once the flood or A* reached a square at floor level,
the walkway square above it counted as visited and the walkway path was never explored (offline simulation:
the south-floor flood reached the ramp top but not the walkway beyond it, though the straight walkway line has
no obstacle). GridPath.Cell now has a Level; searches ask a Neighbour delegate where a step lands (the old
yes/no rule is wrapped by FromStep, so existing tests are unchanged). Nav keeps, per square, a list of surfaces
(CellAt: same surface if within 1.2 m), ground probes cached per square and 1 m of probe height, and route
goals / reachability lookups match the target's height (within 3 m). Tests: FloodFindsTheWalkwayOverTheFloor,
SearchCrossesOverTheWallOnTheWalkway.
