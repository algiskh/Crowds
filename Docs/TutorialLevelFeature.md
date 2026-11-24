# Tutorial level "Учебный тоннель" + campaign order and level logos

The first campaign level is a straight service tunnel. It teaches the basics one lesson at a time: moving, shooting,
ammo, melee, poison and health, the shield (with a weapon pickup and a crowd), and the speed boost.

Hints use the existing UI:
- the objective banner (`DifficultyStage.Objective` → `MissionSystem` → `ObjectiveBanner`);
- the UI log (gate messages, pickup lines).

On top of that, **scene arrows** show where to go.

Build it with **Tools ▸ Crowds ▸ Build Tutorial Level** (`Editor/Levels/TutorialLevelBuilder.cs`). Re-running is safe: assets are rewritten in place and keep their guids.

| Output | Path |
|---|---|
| Level prefab | `_Prefabs/Levels/Level_00_Tutorial.prefab` (+ floor mesh `Meshes/Level_00_Tutorial_Floor.asset`) |
| Level config | `_Data/Levels/Configs/Level_Tutorial.asset` (rewritten on every run, like the Key level) |
| Menu entry | `_Data/Levels/Definitions/Level_Tutorial.asset`, put **first** in `CampaignLevelLibrary` by `CampaignSetup` |
| Arrow prefabs | `_Prefabs/Props/TutorialArrow_Floor.prefab`, `TutorialArrow_Marker.prefab` (+ meshes in `_Prefabs/Props/Meshes`) |
| Melee-only crate | `_Data/Breakables/TutorialCrate.asset` (a copy of `Crate`, id `TutorialCrate`, 50 HP, melee only) |
| Conditions | `_Data/SmartConditions/Mission/`: `PlayerInZone_Tut*`, `LootCollected_*`, `BreakablesCleared_TutorialCrate` |
| Materials | `_Materials/Level/Tutorial_*` |

The builder reuses the Key level's kit: walls (`BulletBlocker`), `LevelGate`, sewer hatches, open wall pipes, zones and
`StageLoot`. If the hatch or gate prefab is missing, the builder creates it.

## Layout

The floor top is y = −0.5. The tunnel is 10 m wide (z −5…5) and runs from x = 0 to x = −160, which is **up the
screen**. It is one `FloorSector` in `Sliding` mode. Six gates (`tg1`…`tg6`) split it into rooms. Each gate opens on its
stage and **stays open**: a lagging zombie can always reach the player, so the level can't softlock.

| Room | x | Contents |
|---|---|---|
| S0 | 0…−16 | Start (−3, 0) facing −X. Zone `t_move` in front of `tg1`. |
| S1 | −16…−40 | 3 open wall pipes (spawn points: Slow). Ammo at (−30, 0). |
| S2 | −40…−60 | Melee-only crate at (−50, 0) on a yellow floor plate. |
| S3 | −60…−88 | A collapsed section: two concrete blocks leave a 3 m lane (x −66…−76) over **5 strong toxic hatches** (5 HP / 0.4 s, player only), with hazard bands at both ends. Zone `t_toxic_exit` is past the lane. Medkit at (−83, 0). |
| S4 | −88…−116 | Shield at (−94, 0). Shotgun + 12ga at (−97, 0). 4 wall pipes (Slow / Fast). |
| S5 | −116…−150 | Speed boost at (−121, 0). |
| Exit | −150…−160 | Green exit marker, zone `t_exit`. |

Dressing is visual only and excluded from the bake:
- painted lines: a dashed yellow centre line and white edge lines;
- cable trays on both walls and two service pipes on the north wall;
- caged emissive wall lamps every 7 m;
- hazard stripes under every gate.

## Stage script

| Stage | Objective (banner) | Ends when |
|---|---|---|
| tutorial | "Учебный тоннель. {Move} — ходить, мышь — целиться. Иди по стрелкам к воротам" | The player is in `t_move`. |
| level1 | "Зомби! Наведи прицел и стреляй — {Attack}. Убей всех". Quota: 3 Slow. | `MobsCleared` |
| level2 | "Патроны на исходе — подбери коробку с патронами. Перезарядка — {Reload}" (9mm × 20 appears) | `LootCollected_Ammo` |
| level3 | "Пули этот ящик не берут. Подойди вплотную и ударь — {Melee}" | `BreakablesCleared_TutorialCrate` (drops 9mm × 15) |
| level4 | "Люки впереди ядовиты, обойти их нельзя. Пробеги — не задерживайся" | The player is in `t_toxic_exit`. |
| level5 | "Яд отнял здоровье — подбери аптечку" (Health 50) | `LootCollected_Health` |
| level6 | "Щит на время поглощает урон — подбери его" | `LootCollected_Shield` |
| level7 | "Возьми дробовик и отбейся от толпы, пока держится щит". Quota: 6 Slow + 2 Fast. | `MobsCleared` |
| level8 | "Ускорение — подбери его, впереди забег" | `LootCollected_SpeedUp` |
| level9 | "Беги к выходу! За тобой погоня — черлидерши не отстанут". The chase lasts 12 s, and spawning speeds up ×2 over that time (`SpeedMultiplier` 2, inter-spawn 0.3 s). Fast zombies (1.3 s) and zombie cheerleaders `CheerZombie` (1.8 s) pour out of the 4 S4 pipes behind the player. Two more pipes ahead at x −138 send cheerleaders (3.5 s) across the run. | The player is in `t_exit`. |
| finish | — | Victory. |

Config rules:
- **Start state.** The player starts as usual: Pistol, 10 rounds, reserve `MainHolder.StartAmmo` (0). So after the first fight the magazine really is low, and the ammo lesson makes sense.
- **Mob drops.** Weapons are blocked (`AllowedMobWeaponDrops = ["__none__"]`). Bonuses are blocked (`shield`, `speedup`, `rage`). Ammo and health drops are still allowed.
- **Ammo fallback.** The same as the other levels (`LowAmmoNoneAround`, 10 rounds). There are 6 `AdditionalSpawn` points, in zones `t_s1`, `t_s2`, `t_s4` and `t_s5`.
- **Spawn window.** `_spawnDistance` = (2.5, 40).

## New runtime pieces (reusable)

| Piece | File | Role |
|---|---|---|
| `TutorialArrow` | `Mission/TutorialArrow.cs` | A scene hint visible only during its `_stages`. `Floor` style: a flat orange chevron that swells and nudges forward. Chains pulse one after another (`_phase`). `Marker` style: a big arrow floating above a target, pointing down, bobbing and spinning. The object is simply switched on and off, so hidden arrows cost nothing. The prefabs are saved inactive. |
| `MissionComponent.Arrows` | `Components.cs`, `EntryPoint.RegisterMissionObjects` | Found together with the gates, keys, zones and stage loot, including inactive objects. `MissionSystem.OnStageStart` calls `ApplyStage(level)` on each arrow. |
| `InputHintFormatter` | `UI/InputHintFormatter.cs` | Replaces `{ActionName}` tokens in the objective text with the current key names. Rebinds are respected. Keyboard and mouse are preferred; if the gamepad was used last, its bindings are shown. Examples: `{Move}` → "WASD", `{Attack}` → "ЛКМ", `{Melee}` → "СКМ (колесо)", `{Reload}` → "R". `MissionSystem` formats the text once per stage, and the "Осталось: N" counter reuses the formatted text. Unknown tokens are left as they are. |
| `LootCollectedCondition` | `SmartConditions/LootCollectedConditionWrapper.cs` | Fulfilled once matching map or stage loot (`_lootType`, optional `_id`) has appeared and none is left. Mob drops are ignored, since they carry a `LifeTimeComponent` and may simply expire. It uses the same latch as `BreakablesCleared`, which covers the frames before the stage loot actually spawns. |

## Campaign order and level logos

`Editor/Levels/CampaignSetup.cs` adds the menu item **Tools ▸ Crowds ▸ Update Campaign (Order + Icons)**. It also runs at the end of the tutorial and suburb-level builders. It does two things:

- **Order.** Levels are sorted by difficulty using `KnownOrder`: Tutorial → Infinite Scroll → Key → Baseball → Mall → Hockey → Cul-de-sac → Backyards. A level is included if it is built or already in the library. Any other library entries follow in their current order.
- **Icons.** `Assets/Sprites/LevelIcons/<DefinitionAssetName>.png` (256², drawn logos) is imported as a single sprite (no mips, uncompressed) and assigned to the matching `LevelDefinition`. Current logos:
  - `Level_Tutorial`: a tunnel with orange chevrons and a hazard gate;
  - `LevelDefinition` (Infinite Scroll): a suburban street with an ∞ badge;
  - `Level_Key`: a hazard gate, a red lamp and a golden key;
  - `Level_Hockey`: a rink with crossed sticks;
  - `Level_CulDeSac`: a turnaround with an island tree and a barricade;
  - `Level_Backyards`: yards with a pool, a trampoline and a shed by the gravel alley.
  - `Level_Baseball`: an infield diamond with a bat and ball;
  - `Level_Mall`: rack aisles seen from above with a big shopping cart.

  The logos were drawn by a script outside the project. To replace one, overwrite the PNG and re-run the menu item.

## Not verified yet

- The level has not been built in Unity or run in Play Mode yet. Check:
  - the navmesh (the lane between the concrete blocks, the pipes);
  - that the arrows show and hide per stage, and that the marker arrows hover above the loot icons;
  - the poison amount in the lane: about 20–25 HP at a run. To tune it, change `stench.Value` / `Interval` in the builder;
  - the pace of the final run.
- Key names come from the "Player" action map. A gamepad shows its own names (for example "Right Trigger") through `InputControlPath`.
