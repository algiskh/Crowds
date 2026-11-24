# Hockey Level ("Ледовая арена"), Ice Sliding, Modifier Zones, Quota Waves — Reference

This is the 3rd campaign level: a hockey rink inside a small arena enclosed by primitive stands. The goal is to survive 3 waves. On the ice, the player and mobs move inertially: they keep sliding and struggle to turn or stop.

## Building

Run **Tools ▸ Crowds ▸ Build Hockey Level** (`Editor/Levels/HockeyLevelBuilder.cs`). Re-running it rewrites the assets in place. It reuses the `KeyLevelBuilder` helpers (now `internal`): materials, primitives, spawn and loot helpers, conditions, and the `wall_spark` effect.

| Output | Path |
|---|---|
| Level prefab | `_Prefabs/Levels/Level_03_Hockey.prefab` (+ `Meshes/Level_03_Hockey_Floor.asset`, `Level_03_Hockey_Ice.asset`) |
| Level config | `_Data/Levels/Configs/Level_Hockey.asset` |
| Menu entry | `_Data/Levels/Definitions/Level_Hockey.asset`, inserted as **#3** in `CampaignLevelLibrary` (after "Запертая дверь") |
| Mob config | `_Data/Mobs/GunnerBare.asset`: a copy of `GunnerMob` without the helmet and with the original rifle (Mini-14, single shots, cooldown 0.8 s, 50 HP; the AR, 3-round burst and 75 HP stay with the helmeted `GunnerMob`), added to `MobConfigHolder` |
| Materials | `_Materials/Level/Level_Ice` (seamless scratched-ice texture `Assets/Textures/Ice.jpg` via `LevelTextures`, tinted to keep the grey-blue average, one tile per 8 m), `Level_IceLine*`, `Level_Board*`, `Level_Seat*`, `Level_GoalNet` |

The campaign menu (`MainMenuController`) lists every entry of `CampaignLevelLibrary`. The `Content` of the level list in `MainMenu.unity` had no layout, so every button was created at the same spot and only one level was visible. `MainMenuController.EnsureListLayout` now adds a `VerticalLayoutGroup` + `ContentSizeFitter` at runtime when the scene has no layout group. Once both builders have run, the order is Level 1 → "Запертая дверь" → "Ледовая арена".

## Layout

Floor top is at y = −0.5. The floor is one `FloorSector` (Sliding). Rendering layer mask 5 lets it receive decals.

| Part | Where | Notes |
|---|---|---|
| Arena floor | x −36…36, z −20…20 | Concrete floor around the rink. The ring outside the boards is deliberately narrow (≈4.5 m on the long sides, ≈5.5 m behind the goals) so running laps outside the rink isn't attractive. |
| Rink (ice) | 60 × 30 rounded rectangle, corner R 8.5, centred | The ice mesh sits 2 cm above the floor, receives decals, and has the `IceRink` zone. Markings: centre and blue lines, goal lines, dots, creases. Two goals, which are small solid obstacles. The nets are double-sided cut-out quads with a procedural **256×256 RGBA diamond-mesh texture** (`_Materials/Level/Textures/GoalNet.png`, alpha = the mesh, coverage-preserving mips; `Level_GoalNet` is enforced on every run to alpha-clip, cull off, tiling 2×2). |
| Boards | Along the rink outline, 1.1 m high | White, with a yellow kick plate and a red rail. Two gates on each long side, at x −12…−9 and 9…12. The boards cut the navmesh but don't stop bullets: they are low enough to shoot over. |
| Stands | All four sides, outside the arena edge | A 1.4 m parapet (cuts the navmesh; its `BulletBlocker` throws `wall_spark`) plus 4 stepped tiers with red or blue seat rows. The long sides run past the corners to close them; the corner squares past x = ±36 are bare concrete steps without seats (seats there faced the side of the east stand). Except the two west stage corners: the north/west stands are cut for the cheerleader stage and the south/west stands for the music stage (`ComputeStageCorner(±1)`), each closed by a diagonal corner stand (`Stand_CheerCorner`, `Stand_MusicCorner`). |
| Outer walls | Behind the stands on all four sides (`OuterWalls`) | White-grey (`Level_OuterWall`) with darker pilasters every ≈8 m and a cornice (`Level_OuterWallTrim`). Inner face flush with the back of the top tier (x ±41.2, z ±25.2), 0.4 m thick. The far walls (west/south = top of the screen) are 8 m, the near ones (north/east, between the camera and the arena) 6 m so they don't hide the player at the parapet. A concrete backfill 2 cm under the floor level covers the stands' footprint (hides the void behind the diagonal corner stands). Visual only: ignored by the bake, no shadows. |
| Mob tunnels | 3 on each long side (x = −25, 0, 25); none on the short sides (they sat right behind the goals) | A 3 × 2.4 m hole cut through the parapet and the first two tiers (3.2 m deep), lined with **pitch-black unlit** material (`Level_TunnelBlack`) and topped by a roof slab, with a pillar/lintel frame in front. The tunnel floor is the navmesh area **`MobOnly`** (index 3, named by the builder in `ProjectSettings/NavMeshAreas.asset`). Mobs path with AllAreas and walk out. The player's `NavMeshAgent` excludes it (`EntryPoint`, after the bake), and so do the escaping cheerleaders, so neither can walk in. The spawn point sits 2 m deep inside, so mobs come out of the darkness. The back wall is a `BulletBlocker`. |
| Player start | Centre ice (0, 0), facing north | |
| Overturned vending machine | Outside the south board gate at x 9…12 (centre 9.6, −16.25, yaw 70°) | Red soda machine lying on its back (glass, buttons, logo band, spilled cans). Solid for the bake, no `BulletBlocker` (0.8 m, shoot over it). Leaves ≈1.4 m of the gate and ≈2.4 m of corridor behind it. |
| Ice resurfacer | On the ice in the corner opposite the cheerleaders (19.5, −10.5), yaw −55° | Generic primitive machine (no branding): white body on 4 tyres, seat + steering wheel, snow tank with a blue band, rear conditioner board, headlights, tail lights, amber beacon. Solid for the bake. The chassis and tank are `BulletBlocker`s, so it works as cover on the ice. |
| Cheerleader stage | World corner −X/+Z (screen right), on the diagonal from the rink corner | 7 × 5 m deck at 0.9 m set into the stands: the north/west stands are cut back and a diagonal `Stand_CheerCorner` closes the corner behind it. Only a 2.6 m passage is left between the boards and the stage front. 3 cheerleader NPCs face the ice and the camera. See `Docs/CheerleaderFeature.md`. |
| Music stage | World corner −X/−Z (screen top-left), the Z-mirror of the cheerleader stage (`MusicStage`) | Same 7 × 5 m deck at 0.9 m set into the stands (red skirt), the same 2.6 m passage. Speaker stacks in the back corners, a boom mic stand with a floor wedge monitor at the front centre, an electric guitar (`Level_GuitarBody`/`Level_GuitarNeck`) resting on a tripod stand next to a combo amp. Decor only: no musicians yet; empty `Slots/Slot_Vocal`, `Slot_Guitar` mark where they will stand (facing the ice). Not Walkable. |
| Scoreboard | West stand (behind the west goal), centred on z = 0, 1 m closer to the arena than the top tier (`ScoreboardInset`; the posts stand on the tier below) | The fixed camera looks −X, so this stand faces the screen. Old seven-segment board (amber segments, faint unlit ones visible), tilted back 20°, red HOME / blue AWAY plates. `ArenaScoreboard` (static registry) + `ECS.ScoreboardSystem` (after `FailSequenceSystem`): home = player deaths (1 once `FailSequenceComponent.Phase` leaves `Inactive`), away = `FragCountComponent`; during a breather (stage with `ShowTimer`, no `MobQuota`, no end conditions) the away side shows the countdown in whole seconds (ceil of `DifficultyComponent.DifficultyTimer`) while the player is alive. 3 + 3 cells; the digits grow outward from the colon (home right-aligned, away left-aligned) and unused cells stay dark, like `(888):51(8)`. Home shows `0` while alive (`_homeBlankZero` off); away 0 = a single `0`. Alive: `  0:81`, after death: `  1:81`. More than 999 is clamped. |
| Video screens | Either side of the scoreboard on the west stand (`ArenaScreens`: `Screen_Left` at −Z, `Screen_Right` at +Z) | 4.4 × 2.5 m, same posts, frame and 20° tilt, centred on the scoreboard's height. Not working yet: a glossy black `Panel` (`Level_ScreenOff`) and a dim amber standby LED. Put a video material / RenderTexture on `Panel` to switch them on. |

## Stage script (`Level_Hockey.asset`)

| Stage | What happens | Ends when |
|---|---|---|
| level1 | Wave 1/3, quota **25 Slow + 15 Fast**. 5 s of rest at the start. 9mm ammo at centre ice. | The quota is spent and all mobs are dead (`MobsCleared`). |
| level2 | Breather, 12 s with a timer on screen. Loot is thrown onto the ice: SMG + 9mm, health + grenade, ammo for the current weapon + 9mm. | After 12 s. |
| level3 | Wave 2/3, quota **70 Fast + 8 Fatman**. | The quota is spent and all mobs are dead. |
| level4 | Breather, 12 s. Loot: AR + 5.56, health + grenade, ammo for the current weapon + health + the `rage` bonus (`Loot_Break2_South`). | After 12 s. |
| level5 | Wave 3/3, quota **5 GunnerBare + 5 GunnerMob (helmet) + 45 Fast + 40 Slow** (Slow spawn presets on level5, cooldown 2.5). | The quota is spent and all mobs are dead. |
| finish | — | Victory. |

The banner shows the wave objective plus "Осталось: N", where N = alive mobs + quota left + pending spawns.

Loot rules:
- Mobs never drop a Shotgun, SawedOff, 12ga ammo, or the `shield` bonus. The weapon whitelist is `Pistol, SMG, AR, Rifle, MachineGun`, plus `_bannedMobAmmoCalibers = [Gauge12]` and `_bannedMobBonusIds = [shield]`.
- Ammo fallback, as in "Запертая дверь": when the current caliber runs low (`LowAmmoNoneAround`), 10 rounds drop at one of 4 points on the ice.

## Ice sliding (reusable)

| Piece | File | Role |
|---|---|---|
| `TractionModifier` | `Modifier.cs` | Value = grip, in m/s² of velocity change. If an owner has several, the lowest wins (`Utils.GetMinModifier<T>`). Id `TractionIce`. |
| `InertiaComponent` | `Components.cs` | Velocity plus last-step bookkeeping. Added lazily the first time an entity meets slippery ground. |
| `Inertia` | `ECS/Inertia.cs` | With grip, the real velocity is steered toward the desired one by at most grip·dt per frame. Without grip, the velocity tracks the desired one, so momentum carries over onto the ice. If last frame's step was mostly blocked (the NavMeshAgent was clamped at the boards or the stands, or the crowd was in the way), the velocity resets to what actually moved: the owner slides along walls instead of pushing into them. |
| Player | `PlayerMovementSystem` | On ice, keeps sliding without input. The `NavMeshAgent` keeps the player on the navmesh. The player is heavier than the zone's grip: grip / `PlayerConfig.IceInertia` (1.2 = 20% more inertia: 3 → 2.5 m/s²; the skates' counter-steer multiplies that). Frozen by the pause menu (`IsWorldFrozen`, like `MoveSystem`): no sliding while paused, momentum kept for after. Win/fail pause, death and locked controls stop the slide dead (`Inertia.Stop`, also for every mob in `GameOverActions.StopAllMoves`). Player and mob movement integrate `MoveDeltaTime()` = `Time.deltaTime` clamped to 0.05 s, so a hitch or a return to the window no longer throws the player forward by metres. |
| Mobs | `MoveSystem` | `MoveByPath` and `MoveDirect` go through `Inertia`. On ice a waypoint counts as reached at 0.75 m instead of 0.05 m, so a sliding mob doesn't orbit it. The player is skipped in `MoveDirect`: the player also has a `MoveComponent`, so `MoveDirect` must never touch the player's `InertiaComponent`. An earlier version reset it to zero every frame, and the player couldn't start moving on the ice. The blocked-step check runs only above 1 m/s, so the start of an acceleration isn't mistaken for a wall. |
| `IceRink` | `IceRink.cs` | A `ModifierZone` shaped as a rounded rectangle (`_size`, `_cornerRadius`). The default modifier is `TractionModifier` 3 m/s² with a 0.1 s linger. |
| Mob skid | `MobSkidSystem` | After `MoveSystem`, before `AnimationSystem`/`CrowdRenderSystem`. On ice, a mob whose real velocity (`InertiaComponent.Velocity`) disagrees with `MoveComponent.Direction` by more than 55° (or that glides with no intent) at a speed of at least max(0.8, 0.4·Speed) is "skidding": enter after 0.12 s, leave below 30° / half that speed after 0.25 s. While skidding it turns to face the player and plays **Skid** (`AnimationType.Skid`, state `skid`, looped; the Fast zombie and the cheer zombie use Mixamo "Wobbling" via `FastMob_ModelSetup` / `CheerZombieMob_ModelSetup`) — or **Idle** if the mob's view has no `skid` state/VAT clip (resolved once per skid: `SimpleAnimator.HasState` / `CrowdAnimationLibrary.TryGetClip`) — through `AnimationStateComponent.Override`/`OverrideActive`. Gameplay systems keep owning `Requested`; both views read `Effective`. Attack/Aim/Shoot/Throw/Die are never overridden. State lives in `InertiaComponent.Skidding`/`SkidTimer`. |

The ice is grey-blue (`IceColor` 0.5 / 0.6 / 0.68, smoothness 0.85), not white, so loot and its ground aura (`LootAura`, see `LootFeature.md`) read well on it. The builder enforces this colour on re-run.

Tuning: the grip is `HockeyLevelBuilder.IceGrip` (3). Lower is slipperier: 3 m/s² means a mob running at 5 m/s needs about 1.7 s to stop; the player (grip 3 / `IceInertia` 1.2 = 2.5) needs about 2 s.

## Modifier zones (generalised oil slick)

`ModifierZone` (`ModifierZone.cs`) is an abstract floor area. It holds `[SerializeReference] Modifier[] _modifiers` and `_linger`, keeps a static registry of active zones, and leaves `Contains()` to subclasses. There are two subclasses: `OilSlick` (blob shape, `SpeedModifier` 0.75) and `IceRink`.

`ModifierZoneSystem` (`ECS/ModifierZoneSystem.cs`) replaced `OilSlickSystem` and runs right before `ModifiersSystem`. For the player and every mob inside a zone, it adds each zone modifier once, as a clone with no VFX. Every frame inside, it refreshes the modifier's Value and Lifetime (= linger). The match key is concrete type + `Id`, so modifiers never stack. Any modifier type works: speed, traction, or damage over time (`DamageModifier` for a toxic puddle, for example).

## Quota waves (reusable)

| Piece | File | Role |
|---|---|---|
| `DifficultyStage.MobQuota` (`MobQuotaEntry[]`: MobId, Count) | `LevelConfig.cs` | An exact number of mobs per id for the stage. The timer only covers rest and minimum time. |
| `WaveQuotaComponent` + `WaveQuota` | `Components.cs`, `ECS/WaveQuota.cs` | Runtime remainder per id. `Sync` rebuilds it when the stage changes; `Spend` decrements it. |
| `SpawnPointSystem` | | In quota stages, spawn points pick only ids with quota left (`SpawnPoint.TryGetRandomSpawnConfig(level, filter, …)`) and stop when the quota is spent. Spawn points still need presets for the stage. The preset cooldowns and weights set the pace, and the quota sets the totals. |
| `MobsClearedCondition` | | Also waits for `WaveQuota.RemainingTotal == 0`, so a lull between spawns doesn't end the wave. |
| `MissionSystem` | | Shows "Осталось: N" for quota waves from the start of the wave. |

## Skates (speed bonus on this level)

On this level the regular `speedup` bonus is a pair of skates. Nothing changes in `BonusConfig`: the level overrides it through `LevelConfig._bonusOverrides` (`BonusLevelOverride`, see `Docs/BonusFeature.md`).

| Piece | Where | Role |
|---|---|---|
| Icon | `Assets/Sprites/Bonuses/IceSkates.png` | The builder copies it from `Assets/REFs/ice_skates_logo.png` and imports it as a sprite (max 256). `LootSystem` uses it instead of `BonusConfig.Preview` for every `speedup` loot on this level. |
| `SkatesModifier` | `Modifier.cs` | Granted together with the `SpeedModifier` for the same duration (`BonusSystem`, `ActiveBonus.Extras`). Value = grip multiplier while steering against the slide (`HockeyLevelBuilder.SkatesCounterGrip` = 4, so 3 → 12 m/s²). |
| Counter-steer | `Inertia.Step(…, counterGrip)` | Grip × lerp(1, counterGrip, −cos) where cos is the angle between the current slide and the input. Reversing or braking with input bites up to 4× harder. Turning sideways and gliding without input stay as slippery as before. Player only (`PlayerMovementSystem`). |
| Skates crates | `Props/Crate_Skates_1..3` | One `Crate` in each east corner outside the rink (+X/+Z, +X/−Z), on the diagonal ≈4 m past the boards, and one on the ring behind the west goal at (−33, −6.5), between the goal axis and the music stage (both west corners hold stages). Per-instance loot override (`Breakable._overrideLoot`): always 1 × `speedup`. Excluded from the bake, like the crates in "Запертая дверь". |
| Mob drops | `MobConfig.PossibleLoots` | Fast, FastToxic and CheerZombie already carry `speedup` (Fast: 1%). Mob drops of `speedup` are banned on campaign levels 1–2 (`_bannedMobBonusIds` in `Level1`/`Level_Key`; `KeyLevelBuilder` writes it too). From level 3 on they are allowed by default. |

## Loot rules per level

`LevelConfig.IsMobLootAllowed(PossibleLoot)` combines three rules: the weapon whitelist (`_allowedMobWeaponDrops`), `_bannedMobAmmoCalibers` (an explicit caliber; `None` = "current weapon" is never banned), and `_bannedMobBonusIds`. `LootSystem.FilterMobWeaponDrops` turns a forbidden mob-drop entry into "no loot". Map and stage loot are not affected.

## Not verified / limits

- Not run in Play Mode yet. Check how the sliding feels for both the player and mobs, mobs getting through the 4 board gates, and the spawn pace (cooldowns in the builder's `mobs` array).
- With 6 tunnels, the pace caps at roughly 2–4 mobs/s (`InterSpawnCooldown` 0.25–0.35 s). `MainHolder.ActiveMobLimit` (160) caps how many are alive at once.
- Mobs collect only at the board gates on their way onto the ice. If that creates jams, add more gaps in `BoardGaps`.
- Hand edits to the prefab are lost on re-run.
