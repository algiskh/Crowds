# Crowds — Project Context

Top-down Unity shooter with an ECS architecture. "Crowds" of mobs attack the player, who shoots back and collects loot.

## Stack

- **Unity 6000.3.10f1** (URP 17.3)
- **Leopotam EcsLite** — ECS framework (github)
- **Sirenix Odin Inspector** — `[BoxGroup]`, `[Title]`, `[Required]`, `[Button]` attributes throughout the inspectors
- **Unity Input System** (new) — `InputSystem_Actions.inputactions` in `Assets/`
- **UniTask** — `com.cysharp.unitask`
- **Unity AI Navigation** (NavMesh) — for mob pathfinding
- **MPUIKit**, **ParticleEffectForUGUI**, **UniText** — UI plugins

## Architecture

**Entry point:** `Assets/_Scripts/ECS/EntryPoint.cs` — a `MonoBehaviour` that creates the `EcsWorld`, `EcsSystems`, registers all singleton entities and systems in `Awake`, and runs `_systems.Run()` in `Update`.

**Singleton pattern:** much of the "singleton" data is stored as a single entity with a single component (`MainHolderComponent`, `SpawnRequestComponent`, etc.). Access goes through the extensions in `Extensions.cs`:
- `world.CreateSimpleEntity<T>()` — create a singleton entity
- `world.GetAsSingleton<T>()` / `TryGetAsSingleton<T>(out value)` — get it
- `world.GetAsSingleton<T1,T2>()` — with an extra filter
- `world.ForEachComponentInTheWorld<T>(action, deleteAfter)` — iteration
- `world.DeleteAllWith<T>()` — delete all entities with the component

**All components** are declared in a single file, `Assets/_Scripts/ECS/Components.cs` (structs).

**Systems** (`IEcsRunSystem`) — one file per system in `Assets/_Scripts/ECS/`. Registration order matters, see `EntryPoint.RegisterSystems()`.

**Configs** — ScriptableObjects in `Assets/_Scripts/SO/`. The root is `MainHolder` (references to `MobConfigHolder`, `GunConfigHolder`, `EffectsHolder`, `DecalsConfigHolder`, `SoundHolder`, `SpriteHolder`, `PlayerConfig`). Levels are `LevelConfig`.

**Pools** (Mob, Bullet, Effect, Decal, Loot, MapLoot) — singleton components holding `Stack<>`/`List<>`/`Dictionary<>` plus a parent `Transform`.

## `Assets/_Scripts/` Structure

- `ECS/` — systems, `Components.cs`, `EntryPoint.cs`, `Extensions.cs`
- `SO/` — ScriptableObject configs
- `UI/` — MonoBehaviour views for UI (`AmmoCounter`, `FailWindow`, `DifficultyTimerView`, `ValueBar`)
- `Animation/` — `SimpleAnimator` (Animator cross-fade wrapper, hash-cached), `AnimationType` (+ `AnimationTypes` enum→state-name/hash map)
- `SmartConditions/` — condition wrappers (`FragsConditionWrapper`, `NoAmmoAroundConditionWrapper`)
- `Enums/` — enum types
- `Input/InputActionsHolder.cs`
- `Remote/Leaderboard/` — leaderboard
- `_Scripts/` root — scene MonoBehaviours: `Player`, `Mob`, `Weapon`, `Bullet`, `Loot`, `SpawnPoint`, `FloorSector`, `NavMeshManager`, `AimVisualizer`, `PathGizmoDrawer`, plus enums like `DamageType`, `AimType`, `BonusType`

## Assets

- `Assets/_Prefabs/` — gameplay prefabs (`Mobs/`, `UI/`, `MapLoot/`, `Effects/`, `Decals/`, weapons `762Piercer.prefab`, `Bullet.prefab`, `Shell.prefab`, `FloorSector.prefab`, `Loot.prefab`)
- `Assets/_Data/` — ScriptableObject assets
- `Assets/Scenes/SampleScene.unity` — main scene
- `Assets/_Animations/`, `_Materials/`, `_Sounds/`, `Shader/`, `Sprites/` — resources
- Vendor folders: `Epic Toon FX/`, `MPUIKit/`, `UniText/`, `Modern GDR - Free icons pack/`, `Plugins/`

## Conventions

- Namespace `ECS` for systems/components (though components live in the global namespace).
- MonoBehaviour fields are private with `[SerializeField]`, `_` prefix. Grouped via `[BoxGroup]`.
- Comments and `[Title(...)]` headers are written in English.
- Commit `.meta` files — don't delete or create files behind Unity's back.
- New systems: add an `IEcsRun/Init/DestroySystem` class → register it in `EntryPoint.RegisterSystems()` in the correct order.
- New components: add a `struct` to `Components.cs`.

## Commands

Builds and type checks go through the Unity Editor (open the scene, Play). There's no CLI build in the repo. Before claiming "done on the UI side" — explicitly run it in play mode; there are no automated tests.

## Feature Documentation

- `Docs/ModifierSystem.md` — the shared modifier system (buffs/debuffs/DoT on any entity): the `Modifier` model and subtypes, `ModifiersSystem`, application paths (direct add vs `TryApplyModifierComponent`), consumers (`GetModifier<T>`), effect linking, plus a short bonus summary. Read instead of re-walking the code.
- `Docs/GrenadeFeature.md` — grenade system (loot → hold-to-charge throw with aiming → arcing flight → radial explosion). Read instead of re-walking the code.
- `Docs/BonusFeature.md` — pickable bonuses (speed up, shield) on top of the shared modifier system: `BonusConfig`/`BonusConfigHolder` SOs, `LootType.Bonus`, `BonusSystem`, speed/shield bars in `PlayerStats` with a timer. Read instead of re-walking the code.
- `Docs/MeleeMobFeature.md` — melee mob with a telegraphed attack (like the player's): windup→strike→cooldown phases in a single `attack` animation, `MeleeMobConfig : MobConfig` + shared `MeleeConfig`, `MeleeAttackerComponent`/`MeleeAttackerSystem`, contact damage disabled in `CollisionSystem`. Read instead of re-walking the code.
- `Docs/RangedMobFeature.md` — ranged mob: telegraphed shot (windup→shot→cooldown in the `attack` animation), `RangedMobConfig : MobConfig` with an embedded `GunConfig` (like the player's), `RangedAttackerComponent`/`RangedAttackerSystem`. Key: `BulletComponent.Team` (`BulletTeam` Player/Enemy): player bullets hit mobs/breakables, enemy bullets hit the player (distance sweep check; the player has no collider map). Reuses the whole player bullet pipeline. Also fixes: unset `Bullet.Radius`, early exit of `BulletOverlapSystem` with no bullets, activation of `GunConfig.ShotDebuffs`, recycle-safety of `RequestDamage.DamageModifiers`. Read instead of re-walking the code.
- `Docs/LootFeature.md` — the loot system end to end: types (`LootType`), spawn sources (`RequestSpawnSource`: mob/map/bonus spawn), flow `DamageSystem`→`RequestLootSpawn`→`LootSystem` (drop-table roll, sprite selection, pool), pickup in `CollisionSystem` by type, and a **configurable despawn timer for mob loot (per type, on `MainHolder`)**. Ground aura under every loot item (`LootAura`: bright tinted ring + dark halo, readable on dark and light floors). Read instead of re-walking the code.
- `Docs/AmmoSystem.md` — per-caliber ammo (enum `Caliber`): the reserve lives in `AmmoInventoryComponent` (dictionary `Caliber`→count), weapons with the same `GunConfig.Caliber` share the pool; the magazine stays in `WeaponComponent`. Ammo loot carries the caliber in the `AmmoCaliber` field (`None` → current weapon's caliber). New caliber = add an enum member with `[InspectorName]`. `AmmoConfig`/`AmmoConfigHolder` (on `MainHolder`) define the projectile prefab per caliber (removed from `GunConfig`, fallback `MainHolder.BulletPrefab`) and the loot icon. Read instead of re-walking the code.
- `Docs/SectorFeature.md` — floor sector system under the player: two modes in `LevelConfig.SectorMode` — `Recycling` (infinite scroll, 3 sectors reused with object carry-over) and `Sliding` (finite level, pre-placed sectors toggled by a window around the player). `CheckSectorSystem`, `NavMeshManager`, `FloorSector`. Read instead of re-walking the code.
- `Docs/FailSequenceFeature.md` — cinematic ending on player death: phases control lock → red veil → menu+pause at 0.5s (`FailSequenceSystem`, `FailSequenceComponent`, `InputLockComponent`, runtime overlay `FailScreenOverlay`), shared `GameOverActions.StopAllMoves`, working restart button. Read instead of re-walking the code.
- `Docs/ControlsSettingsFeature.md` — control rebinding: binding serialization to JSON (Input System `SaveBindingOverridesAsJson`) in PlayerPrefs via a replaceable store (`IControlSettingsStore`/`ControlSettings`), settings page with a control list (`ControlsSettingsMenu` builds rows from the `InputActionAsset` into a `ScrollRect`+`VerticalLayoutGroup`, `ControlBindingRow` — name/key/gamepad, interactive keyboard/mouse rebinding). Read instead of re-walking the code.
- `Docs/CrowdRenderingFeature.md` — converting a regular rigged FBX mob (SkinnedMeshRenderer + Animator) into a GPU-instanced Vertex Animation Texture (VAT): offline animation baking into a texture (`VatBakerWindow`, **Tools ▸ Crowds ▸ VAT Baker**), URP shader `CrowdVat`, `CrowdAnimationLibrary` asset, runtime rendering `CrowdRenderSystem` via `Graphics.DrawMeshInstanced`. Optional and hybrid: a mob uses VAT only if its `MobConfig` has a `CrowdLibrary` set (otherwise the classic skin); movement/collisions/healthbars/pool untouched. Read instead of re-walking the code.
- `Docs/LoadingScreenFeature.md` — loading curtain between menu/restart and the ready level: runtime overlay `LoadingScreen` (spinner + `UniText` + progress bar, `DontDestroyOnLoad`, unscaledDeltaTime animation), non-blocking boot `EntryPoint.InitializeAsync` (UniTask, per-frame steps), async navmesh baking (`NavMeshManager.Configure(bake:false)` + `RebuildNavMeshAsync`), `LoadSceneAsync` from menu/restart windows. Read instead of re-walking the code.
- `Docs/LevelEventsFeature.md` — level script events: an empty `LevelEventTrigger` object with a list of entries, each arming at the start of the required `DifficultyLevel` (optional gate from `SmartConditionWrapper[]`) and spawning breakables (`SpawnBreakableAction`). `LevelEventSystem` (after `AdditionalLootSpawnSystem`), `LevelEventHolderComponent`/`LevelEventObserverComponent`. Next-stage start gated on destruction — via `BreakablesClearedCondition` in `DifficultyStage.EndConditions` (latch `_sawAny` against a start race). Read instead of re-walking the code.
- `Docs/BreakableFeature.md` — breakable environment (crates/barrels/walls): carves navmesh via carving `NavMeshObstacle` (mobs path around, no rebaking), damage only from configurable sources (`BreakableDamageSources`: bullets/explosions/melee/mob contact; a bullet is blocked and dies), on destruction — staged effects at mesh points (`RequestEffectComponent.Delay`), loot scatter, outcome Vanish/Debris + force-repath of nearby mobs. Configs in `BreakableConfigHolder` (on `MainHolder`), each carries its prefab; runtime spawn by request with position (`RequestSpawnBreakableComponent` / `world.RequestSpawnBreakable(id,pos)`), pool by id (`BreakableSpawnSystem`). `Breakable`, `BreakableConfig`, `BreakableComponent`, `BreakableSystem`. Read instead of re-walking the code.
- `Docs/PlayerAnimation.md` — player body animation (in Russian, with diagrams): Humanoid Mixamo pipeline, which clips to download per weapon set (`WeaponAnimationSet` Rifle/Pistol on `GunConfig`), generated `Player.controller` (Tools ▸ Crowds ▸ Create Player Animator Controller: Base layer 2D strafe blend trees + Death, UpperBody masked layer Aim/Fire/Reload/Hit + Melee/Throw), `PlayerAnimator` view and read-only `PlayerAnimationSystem` (after `PlayerMovementSystem`); muzzle stays on the root. Automated import: `PlayerModelSetupPreset` + `PlayerModelPipeline` (clips sorted into `Rifle/`/`Pistol/`/`Common/` next to the model, keyword scan → slots, Humanoid import, controller fill, `Body` under the scene `Player`). Read instead of re-walking the code.
- `Docs/GameDesignReview.md` — game-design review of the current mechanics (in Russian): weapon DPS/range table and role overlaps, mob roster and crowd-death issues, loot/ammo economy math, difficulty curve of `Level1`, feedback/UI/meta gaps, plus a prioritized 12-item improvement plan. Check before balancing configs or adding gameplay features.
- `Docs/WeaponModels.md` — primitive-style gun models (Mini14/AR/SMG/Pistol/Shotgun/SawedOff/MachineGun after `Assets/REFs`) generated by **Tools ▸ Crowds ▸ Build Weapon Models** (`WeaponModelBuilder`, specs in code), `WeaponModel` view (grip/muzzle/left-hand target/magazines; `MobWeapon` is a subclass), `GunConfig.Model` shown by `PlayerAnimator` with the magazine hidden during reload, and `WeaponHandIK` (two-bone left-hand IK on the forend, Generic rigs; on `Gunner.prefab`). Read instead of re-walking the code.
- `Docs/PauseMenuFeature.md` — Esc/Start pause menu (Continue / Main menu): `Pause` input action, runtime `PauseMenu` overlay, `PauseMenuSystem` (before `PauseSystem`, `SignalSource.UI`). Read instead of re-walking the code.
- `Docs/DamageFeedbackFeature.md` — player injury feedback: red edge vignette proportional to the hit + low-HP pulse, short camera shake (`CameraShakeSystem` registered twice around `FollowSystem`), edge arc toward the hit source (`RequestDamageComponent.HasSource/SourcePosition`); `PlayerDamagedEventComponent` from `DamageSystem` → `DamageFeedbackSystem` → runtime `DamageFeedbackOverlay`; settings in `PlayerConfig.DamageFeedback`. Read instead of re-walking the code.
- `Docs/HelmetFeature.md` — mob helmets: `Headwear` prefabs (construction hard hat, PASGT) built by **Tools ▸ Crowds ▸ Build Helmet Models** (`HelmetModelBuilder`, procedural shells), `MobConfig.Headwear` fitted to the Mixamo Head→HeadTop bones at instance creation (`MobSpawnSystem.SetupNewInstance`, also used by the pool prewarm); `ToxicHelmet` (Toxic x2 HP, stages 4–6) and the Gunner's AR 3-round burst. Read instead of re-walking the code.
- `Docs/KeyLevelFeature.md` — 2nd campaign level "Запертая дверь" (ref `Assets/REFs/LEVEL_KEY.png`) built by **Tools ▸ Crowds ▸ Build Key Level** (`KeyLevelBuilder`): 3 platforms, wave stages (`DifficultyStage.SpawnOnlyWhileTimer` + `MobsClearedCondition`), gates (`LevelGate`: carving obstacle, OnStage/WithKey, re-close behind the player), walls/gates stop bullets (`BulletBlocker`, checked in `BulletOverlapSystem`), zone-aware ammo fallback (`NoAmmoAroundCondition` options + `AdditionalLootSpawnSystem`), oil slicks that slow via `SpeedModifier` (`OilSlick` ModifierZone + `ModifierZoneSystem`), open wall pipes as spawns, key (`LevelKeyPickup`), zones (`LevelZone` + `PlayerInZoneCondition`), `StageLoot`, objective banner (`DifficultyStage.Objective` → `MissionSystem` → `ObjectiveBanner`), `LevelRoot.PlayerStart`, `LevelConfig` spawn-distance override + mob weapon-drop whitelist, sewer hatches with toxic stench. Read instead of re-walking the code.
- `Docs/HockeyLevelFeature.md` — 3rd campaign level "Ледовая арена" built by **Tools ▸ Crowds ▸ Build Hockey Level** (`HockeyLevelBuilder`, reuses the now-`internal` `KeyLevelBuilder` helpers): rink + boards + primitive stands with tunnel spawns; ice sliding (`TractionModifier` → `Inertia`/`InertiaComponent` in `PlayerMovementSystem`/`MoveSystem`); generic area modifiers (`ModifierZone` → `OilSlick`, `IceRink`; `ModifierZoneSystem`); quota waves (`DifficultyStage.MobQuota`, `WaveQuota`); per-level mob loot bans (`LevelConfig.IsMobLootAllowed`); `GunnerBare` config; props: overturned vending machine, cheerleader corner stage set into the stands, seven-segment scoreboard deaths:frags growing from the colon (`ArenaScoreboard` + `ScoreboardSystem`), ice resurfacer, alpha-mesh goal nets; dark mob tunnels with the `MobOnly` navmesh area excluded from the player agent.
- `Docs/CheerleaderFeature.md` — cheerleader NPCs (not enemies): **Tools ▸ Crowds ▸ Build Cheerleader NPC** (`CheerleaderNpcBuilder`: material, Generic clips named after the takes, controller, `_Prefabs/Npc/Cheerleader.prefab`), `Cheerleader` view + `CheerleaderStand`, pompoms (`Pompom.prefab`, dance only); `CheerleaderSystem`: one synced dance per stand (swap when the group is off screen), not on screen at the wave→breather transition → `CheerZombie` (own model via **Tools ▸ Crowds ▸ Build Cheer Zombie Mob**, 150 HP) + log hint, victory poses, escape on player death with `ZombieTargetComponent` retargeting in `MobPathfindingSystem`; `FriendlyNpcComponent`: while any is alive the fail window opens over a living (unpaused) world; `CheerleaderQuestComponent` for a future achievement. Read instead of re-walking the code.
- `Docs/SuburbFeature.md` — Infinite Scroll (`Level_01`) dressed as an endless liminal American suburb by **Tools ▸ Crowds ▸ Build Suburb (Infinite Scroll)** (`SuburbBuilder`): each recycled `FloorSector` gets a `Suburb` child = `Street_Section` (lawns, sidewalks, curbs, road; visual only, ignored by the navmesh, receives decals) + `Row_A/B/C` (3 houses off the platform: `House_Ranch`/`House_Colonial`/`House_Cape` recoloured per row, `Fence_Picket` runs closing the gaps, front walks/driveways). Prefabs are single baked meshes (submesh per material). Platforms keep their area and colliders. Read instead of re-walking the code.
- `Docs/KnownIssues.md` — backlog of known but deliberately unfixed bugs and optimizations from the code audit (combat, mobs/AI, world/loot, UI, performance), with files/lines and suggested fixes; plus notes on safe EcsLite usage (`EcsPackedEntity` for references living longer than a frame). Check before edits in affected systems.

## Git

- Main branch is `main`, current is `master`.
- `.csproj`/`.sln` are in `.gitignore` — Unity regenerates them; don't commit.
