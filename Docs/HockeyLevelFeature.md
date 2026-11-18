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
| Materials | `_Materials/Level/Level_Ice`, `Level_IceLine*`, `Level_Board*`, `Level_Seat*`, `Level_GoalNet` |

The campaign menu (`MainMenuController`) lists every entry of `CampaignLevelLibrary`. The `Content` of the level list in `MainMenu.unity` had no layout, so every button was created at the same spot and only one level was visible. `MainMenuController.EnsureListLayout` now adds a `VerticalLayoutGroup` + `ContentSizeFitter` at runtime when the scene has no layout group. Once both builders have run, the order is Level 1 → "Запертая дверь" → "Ледовая арена".

## Layout

Floor top is at y = −0.5. The floor is one `FloorSector` (Sliding). Rendering layer mask 5 lets it receive decals.

| Part | Where | Notes |
|---|---|---|
| Arena floor | x −36…36, z −20…20 | Concrete floor around the rink. The ring outside the boards is deliberately narrow (≈4.5 m on the long sides, ≈5.5 m behind the goals) so running laps outside the rink isn't attractive. |
| Rink (ice) | 60 × 30 rounded rectangle, corner R 8.5, centred | The ice mesh sits 2 cm above the floor, receives decals, and has the `IceRink` zone. Markings: centre and blue lines, goal lines, dots, creases. Two goals, which are small solid obstacles. The nets are double-sided cut-out quads with a procedural **256×256 RGBA diamond-mesh texture** (`_Materials/Level/Textures/GoalNet.png`, alpha = the mesh, coverage-preserving mips; `Level_GoalNet` is enforced on every run to alpha-clip, cull off, tiling 2×2). |
| Boards | Along the rink outline, 1.1 m high | White, with a yellow kick plate and a red rail. Two gates on each long side, at x −12…−9 and 9…12. The boards cut the navmesh but don't stop bullets: they are low enough to shoot over. |
| Stands | All four sides, outside the arena edge | A 1.4 m parapet (cuts the navmesh; its `BulletBlocker` throws `wall_spark`) plus 4 stepped tiers with red or blue seat rows. The long sides run past the corners to close them, except the cheerleader corner (north and west stands cut at `ComputeCheerCorner`). |
| Mob tunnels | 3 on each long side (x = −25, 0, 25) and 1 on each short side | A 3 × 2.4 m hole cut through the parapet and the first two tiers (3.2 m deep), lined with **pitch-black unlit** material (`Level_TunnelBlack`) and topped by a roof slab, with a pillar/lintel frame in front. The tunnel floor is the navmesh area **`MobOnly`** (index 3, named by the builder in `ProjectSettings/NavMeshAreas.asset`). Mobs path with AllAreas and walk out. The player's `NavMeshAgent` excludes it (`EntryPoint`, after the bake), and so do the escaping cheerleaders, so neither can walk in. The spawn point sits 2 m deep inside, so mobs come out of the darkness. The back wall is a `BulletBlocker`. |
| Player start | Centre ice (0, 0), facing north | |
| Overturned vending machine | Outside the south board gate at x 9…12 (centre 9.6, −16.25, yaw 70°) | Red soda machine lying on its back (glass, buttons, logo band, spilled cans). Solid for the bake, no `BulletBlocker` (0.8 m, shoot over it). Leaves ≈1.4 m of the gate and ≈2.4 m of corridor behind it. |
| Ice resurfacer | On the ice in the corner opposite the cheerleaders (19.5, −10.5), yaw −55° | Generic primitive machine (no branding): white body on 4 tyres, seat + steering wheel, snow tank with a blue band, rear conditioner board, headlights, tail lights, amber beacon. Solid for the bake. The chassis and tank are `BulletBlocker`s, so it works as cover on the ice. |
| Cheerleader stage | World corner −X/+Z (screen top-right), on the diagonal from the rink corner | 7 × 5 m deck at 0.9 m set into the stands: the north/west stands are cut back and a diagonal `Stand_CheerCorner` closes the corner behind it. Only a 2.6 m passage is left between the boards and the stage front. 3 cheerleader NPCs face the ice and the camera. See `Docs/CheerleaderFeature.md`. |
| Scoreboard | Top tier of the west stand (behind the west goal), centred on z = 0 | The fixed camera looks −X, so this stand faces the screen. Old seven-segment board (amber segments, faint unlit ones visible), tilted back 20°, red HOME / blue AWAY plates. `ArenaScoreboard` (static registry) + `ECS.ScoreboardSystem` (after `FailSequenceSystem`): home = player deaths (1 once `FailSequenceComponent.Phase` leaves `Inactive`), away = `FragCountComponent`. 3 + 3 cells; the digits grow outward from the colon (home right-aligned, away left-aligned) and unused cells stay dark, like `(888):51(8)`. Home 0 = dark (`_homeBlankZero`); away 0 = a single `0`. After death: `  1:81`. More than 999 is clamped. |

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
| Player | `PlayerMovementSystem` | On ice, keeps sliding without input. The `NavMeshAgent` keeps the player on the navmesh. |
| Mobs | `MoveSystem` | `MoveByPath` and `MoveDirect` go through `Inertia`. On ice a waypoint counts as reached at 0.75 m instead of 0.05 m, so a sliding mob doesn't orbit it. The player is skipped in `MoveDirect`: the player also has a `MoveComponent`, so `MoveDirect` must never touch the player's `InertiaComponent`. An earlier version reset it to zero every frame, and the player couldn't start moving on the ice. The blocked-step check runs only above 1 m/s, so the start of an acceleration isn't mistaken for a wall. |
| `IceRink` | `IceRink.cs` | A `ModifierZone` shaped as a rounded rectangle (`_size`, `_cornerRadius`). The default modifier is `TractionModifier` 3 m/s² with a 0.1 s linger. |
| Mob skid | `MobSkidSystem` | After `MoveSystem`, before `AnimationSystem`/`CrowdRenderSystem`. On ice, a mob whose real velocity (`InertiaComponent.Velocity`) disagrees with `MoveComponent.Direction` by more than 55° (or that glides with no intent) at a speed of at least max(0.8, 0.4·Speed) is "skidding": enter after 0.12 s, leave below 30° / half that speed after 0.25 s. While skidding it turns to face the player and plays **Idle** (placeholder for a dedicated skid clip, `SkidAnimation`) through `AnimationStateComponent.Override`/`OverrideActive`. Gameplay systems keep owning `Requested`; both views read `Effective`. Attack/Aim/Shoot/Throw/Die are never overridden. State lives in `InertiaComponent.Skidding`/`SkidTimer`. |

The ice is grey-blue (`IceColor` 0.5 / 0.6 / 0.68, smoothness 0.85), not white, so loot and its ground aura (`LootAura`, see `LootFeature.md`) read well on it. The builder enforces this colour on re-run.

Tuning: the grip is `HockeyLevelBuilder.IceGrip` (3). Lower is slipperier: 3 m/s² means a player running at 5 m/s needs about 1.7 s to stop.

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

## Loot rules per level

`LevelConfig.IsMobLootAllowed(PossibleLoot)` combines three rules: the weapon whitelist (`_allowedMobWeaponDrops`), `_bannedMobAmmoCalibers` (an explicit caliber; `None` = "current weapon" is never banned), and `_bannedMobBonusIds`. `LootSystem.FilterMobWeaponDrops` turns a forbidden mob-drop entry into "no loot". Map and stage loot are not affected.

## Not verified / limits

- Not run in Play Mode yet. Check how the sliding feels for both the player and mobs, mobs getting through the 4 board gates, and the spawn pace (cooldowns in the builder's `mobs` array).
- With 8 tunnels, the pace caps at roughly 2–4 mobs/s (`InterSpawnCooldown` 0.25–0.35 s). `MainHolder.ActiveMobLimit` (160) caps how many are alive at once.
- Mobs collect only at the board gates on their way onto the ice. If that creates jams, add more gaps in `BoardGaps`.
- Hand edits to the prefab are lost on re-run.
