using System;
using Leopotam.EcsLite;
using System.Collections.Generic;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

#region Singleton_entities
/// <summary>
/// Singleton component for holding main configuration data
/// </summary>
///
public struct CurrentLevelConfigComponent
{
public LevelConfig Value;
}

public struct MainHolderComponent
{
	public MainHolder Value;
}

public struct SoundHolderComponent
{
	public SoundHolder Value;
}

public struct EffectsHolderComponent
{
	public EffectsHolder Value;
}

public struct DecalsHolderComponent
{
	public DecalsConfigHolder Value;
}

/// <summary>
/// Singleton for collection of spawnpoints for mobs
/// </summary>
public struct SpawnPointsComponent
{
	public Transform[] Value;
}

/// <summary>
/// Singleton for holding a pool of mobs
/// </summary>
public struct MobPoolComponent
{
	public List<Mob> Value;
	public Dictionary<string, Stack<Mob>> Pools;
	public Transform Parent;
}

public struct BulletPoolComponent
{
	// Instances without a SourcePrefab (legacy MainHolder.BulletPrefab path).
	public Stack<Bullet> Value;
	// Per projectile prefab (AmmoConfig.ProjectilePrefab): a bullet returned by one caliber isn't reused by another.
	// Lives here (not in BulletSystem) so PoolPrewarmer can fill it at load.
	public Dictionary<Bullet, Stack<Bullet>> ByPrefab;
	public Transform Parent;
}

public struct EffectPoolComponent
{
	public List<SceneEffect> Value;
	public Dictionary<string, Stack<SceneEffect>> Pools;
	public Transform Parent;
}
#endregion

public struct MobComponent
{
	public Mob Value;
	public MobConfig Config;
	public float Cooldown;
}

/// <summary>
/// Desired vs applied animation state for a mob. Gameplay systems set <see cref="Requested"/> only;
/// AnimationSystem reconciles it to the view and pushes to the Animator only on change.
/// </summary>
public struct AnimationStateComponent
{
	public Scene.Animation.AnimationType Requested;
	public Scene.Animation.AnimationType Current;
	public bool HasCurrent;
	// One-shot: replay Requested from the start even if it's already playing (e.g. recoil on every burst shot).
	// Consumed by AnimationSystem.
	public bool Restart;
}

/// <summary>
/// Marks a mob as rendered by <see cref="ECS.CrowdRenderSystem"/> via GPU-instanced Vertex Animation
/// Textures instead of a SkinnedMeshRenderer+Animator. Added at spawn when the mob's config has a
/// <see cref="Scene.Animation.CrowdAnimationLibrary"/>. The render system reconciles the requested
/// <see cref="AnimationStateComponent"/> into a baked clip and advances <see cref="ClipTime"/>.
/// </summary>
public struct CrowdInstanceComponent
{
	public Scene.Animation.CrowdAnimationLibrary Library;
	public Scene.Animation.AnimationType CurrentClip;
	public float ClipTime;      // seconds elapsed inside the current clip
	public bool Initialized;    // false until the first clip is applied
	// Per-config multiply tint fed to the CrowdVat _InstColor prop. Set from MobConfig.Tint at spawn.
	// Must default to white (1,1,1,1) — a zero vector would render the mob black.
	public Vector4 Tint;
}

public struct MoveComponent
{
	public Transform Transform;
	public Vector3 Direction;
	public float Speed;
	public IEnumerable<Modifier> SpeedModifiers;
}

public struct MovePath
{
	public List<Vector3> Waypoints;
	public int CurrentIndex;
}

public struct PathRecalculation
{
	public float LastTime;
	public float Interval;
}

/// <summary>
/// Tag-signal: requests an out-of-turn path recalculation (bypassing Interval).
/// Set externally (obstacle changes, dynamic blockers, etc.),
/// cleared in MobPathfindingSystem after processing.
/// </summary>
public struct PathRecalculationRequest { }

public struct HealthComponent
{
	public float CurrentHealth;
	public float MaxHealth;
	public TargetType TargetType;
	// +1 per direct hit that dealt damage (DamageSystem); DoT ticks (RequestDamageComponent.IsPeriodic) don't count.
	// PlayerAnimationSystem plays the hit flinch on change.
	public int HitCounter;
}

public struct RequestDamageComponent
{
	public float Damage;
	// Packed: requests may outlive the frame (e.g. raised after DamageSystem), and the target may be
	// deleted/recycled meanwhile. Use world.PackEntity(target) when raising.
	public EcsPackedEntity Target;
	public IEnumerable<Modifier> DamageModifiers;
	// DoT tick (ModifiersSystem): bypasses the player's post-hit invulnerability and doesn't start it.
	public bool IsPeriodic;
	// Optional world point the hit came from (mob, shooter, explosion center). Only read for the player:
	// DamageFeedbackSystem shows a directional hit marker toward it. HasSource=false → no marker.
	public bool HasSource;
	public Vector3 SourcePosition;
}

/// <summary>
/// Event (one entity per hit, raised by DamageSystem): the player actually took damage this frame
/// (after invulnerability and shield). Consumed and deleted by DamageFeedbackSystem (vignette, shake, hit marker).
/// </summary>
public struct PlayerDamagedEventComponent
{
	public float Damage;
	public float CurrentHealth;
	public float MaxHealth;
	public bool IsPeriodic;
	public bool HasSource;
	public Vector3 SourcePosition;
}

public struct ColliderComponent
{
	public Collider Value;
	public CollisionType CollisionType;
}

/// <summary>
/// Singleton component for managing spawn requests
/// </summary>
public struct SpawnRequestComponent
{
	// Presets
	public float MinCoolDown;
	public float MaxCoolDown;
	// Runtime values
	public float LastSpawnTime;
	public float CurrentCoolDown;
	public bool IsBlocked;
}

public struct SpawnPointComponent
{
	public float Timer;
	public SpawnPoint Value;
}

public struct SpawnTimer
{
	public float LastSpawnTime;
}

public struct MobSpawnRequestComponent
{
	public MobConfig Config;
	public Transform SpawnPoint;
}
#region Player

public struct PlayerComponent
{
	public Player Value;
	public PlayerState State;
}

public struct PlayerInputComponent
{
	public Vector3 Move;
	public Vector3 PreviousMove;
	public bool IsFiring;
	public bool IsMeleeing;
	public float MeleeCooldown;
	// Interact action (hold) completed this frame; read by CollisionSystem next frame (weapon pickup).
	public bool InteractPressed;
}

public struct WeaponComponent
{
	public Weapon Weapon;
	public GunConfig GunConfig;
	public float CoolDown;
	public bool IsFiring;
	public int CurrentMagazineCount;
	public float ReloadTime;
	// Seconds the trigger has been held (GunConfig.SpinUpTime gate); reset on release.
	public float SpinUp;
	// +1 per player shot (WeaponFireSystem); PlayerAnimationSystem fires the "Fire" trigger on change.
	public int ShotCounter;
}

// Player's ammo reserve per caliber. Weapons with the same caliber share one pool.
// The magazine (CurrentMagazineCount) lives in WeaponComponent; the reserve lives here.
public struct AmmoInventoryComponent
{
	public Dictionary<Caliber, int> Ammo;

	public int Get(Caliber caliber)
		=> caliber != Caliber.None && Ammo.TryGetValue(caliber, out var value) ? value : 0;

	public void Add(Caliber caliber, int amount)
	{
		if (caliber == Caliber.None || amount == 0)
			return;
		Ammo.TryGetValue(caliber, out var value);
		Ammo[caliber] = Mathf.Max(0, value + amount);
	}

	// Spends up to amount rounds of the caliber. Returns how many were actually spent.
	public int Spend(Caliber caliber, int amount)
	{
		if (caliber == Caliber.None || amount <= 0)
			return 0;
		Ammo.TryGetValue(caliber, out var value);
		int spent = Mathf.Min(value, amount);
		Ammo[caliber] = value - spent;
		return spent;
	}
}

public struct ReloadingComponent
{
	public float ReloadTime;
	public float ShutteringTime;
}

public struct RequestFireComponent
{
}

/// <summary>
/// Who fired the bullet — determines what it can hit. Player bullets hit mobs/breakable
/// environment (as before), Enemy bullets (ranged mobs, RangedAttackerSystem) hit the player.
/// default = Player: the existing player firing path stays correct without explicit assignment.
/// </summary>
public enum BulletTeam : byte
{
	Player,
	Enemy
}

public struct BulletComponent
{
	public Bullet Bullet;
	public float Damage;
	public float LifeTime;
	public float Radius;
	public BulletCheckType CheckType;
	public BulletTeam Team;
	public Modifier[] Modifiers;
	// How many mobs this bullet may hit (GunConfig.PierceCount, else the prefab's MaxPierceCount).
	public int MaxPierce;
	public FixedList32Bytes<int> PiercedTargets;
}

public struct BulletOverlapComponent
{
	/// <summary>
	/// Mob entity-ids caught by this frame's overlap.
	/// Filled in BulletOverlapSystem, read in CollisionSystem.
	/// No managed allocations: FixedList is an inline value-type.
	/// </summary>
	public Unity.Collections.FixedList128Bytes<int> MobHits;

	/// <summary>
	/// Breakable entity-ids hit this frame (destructible environment). Filled in BulletOverlapSystem,
	/// consumed in CollisionSystem (BulletVsBreakable). Inline value-type, no managed allocations.
	/// </summary>
	public Unity.Collections.FixedList128Bytes<int> BreakableHits;

	/// <summary>
	/// True if an Enemy bullet reached the player this frame (the only target — checked
	/// by distance, not via a collider map). Filled in BulletOverlapSystem, read in
	/// CollisionSystem (BulletVsPlayer).
	/// </summary>
	public bool PlayerHit;
}
#endregion


public struct RequestLootSpawn
{
	public int SourceEntity;
	public PossibleLoot[] PossibleLoots;
	public Vector3 Position;
	public RequestSpawnSource Source;
}

[Serializable]
public struct LootComponent
{
	public Loot Loot;
	public LootType LootType;
	public string Id;
	public float Radius;
	public int Count;
	// LootType.Ammo only: ammo caliber. None = "ammo for the current weapon".
	public Caliber AmmoCaliber;
	// LootType.Weapon only: the "hold Interact to pick up" hint was already logged for this loot.
	public bool PickupHintShown;
}

// For loot placed at map at the start of the level
public struct MapLootPoolComponent
{
	public List<MapLoot> Value;
}

public struct LootPoolComponent
{
	public Stack<Loot> Value;
	public Transform Parent;
}

public struct AmmoCounterComponent
{
	public AmmoCounter Value;
}

public struct BorderComponent
{
	public Transform Transform;
	public bool IsPlayerNearBy;
}

public struct EffectComponent
{
	public SceneEffect Effect;
	public float LifeTime;
	// if effect is associated with modifier: owner entity (packed — the owner may die/recycle while
	// the effect lives) and the modifier itself (link for non-damage modifiers, DamageType == Unknown).
	public bool HasModifierLink;
	public EcsPackedEntity ModifierOwner;
	public Modifier LinkedModifier;
	public DamageType DamageType;
	public bool IsChild;
}

public struct RequestEffectComponent
{
	public string EffectId;
	public Vector3 Position;
	public float Rotation;
	// if effect is associated with modifier
	public Transform Parent;
	public DamageType DamageType;
	public bool HasModifierLink;
	public EcsPackedEntity ModifierOwner;
	public Modifier LinkedModifier;
	// Optional fuse (sec): while > 0 the effect is deferred (counted down in EffectsSystem)
	// before it spawns. 0 (default) = spawn immediately. Used for staggered destruction bursts.
	public float Delay;
}

public struct EndGameComponent
{
	public bool isWin;
}

#region FailSequence
/// <summary>
/// Phases of the cinematic fail sequence on player death. Each lasts FailSequenceSystem.PhaseDuration (0.5s):
///  BlockControls — only player controls are locked (the world keeps running);
///  RedScreen — a red veil gradually fades in over the screen;
///  Menu — the game is paused and the defeat window is shown.
/// Done — the sequence is complete.
/// </summary>
public enum FailSequencePhase : byte
{
	Inactive,
	BlockControls,
	RedScreen,
	Menu,
	Done
}

/// <summary>Singleton: state of the cinematic fail sequence (see FailSequenceSystem).</summary>
public struct FailSequenceComponent
{
	public FailSequencePhase Phase;
	public float Timer;
	// Done with the fail window over a still-running world (friendly NPCs alive); full stop comes later.
	public bool WorldLive;
}

/// <summary>Singleton wrapper over the runtime red-veil overlay (created lazily).</summary>
public struct FailScreenOverlayComponent
{
	public FailScreenOverlay Value;
}

/// <summary>
/// Singleton: locks the player's user input separately from the global pause. While Locked=true,
/// InputSystem/GrenadeThrowSystem ignore input, while the rest of the world may keep running
/// (used in the BlockControls/RedScreen phases of the fail sequence).
/// </summary>
public struct InputLockComponent
{
	public bool Locked;
}
#endregion

public struct FollowTarget
{
	public Transform Target;
	public float Threshold;
	public bool IsAcceleratable;
	public float AccelerationMultiplier;
	public float MaxAccelerationMultiplier;

	public bool MatchTargetSpeedIfFar;
	public float MatchSpeedDistance;
}

public struct FollowerComponent
{
	public Transform Value;
}

public struct FollowerOffset
{
	public Vector3 Value;
}

public struct Looker
{
	public Transform Value;
	public bool FlatBillboard;
}

public struct LookerAtCamera
{
	public Transform Transform;
	public bool FlatBillboard;
}

public struct LookAtCursor
{
	public Transform Transform;
	public bool Mode3D;
}

public struct CameraComponent
{
	public Camera Value;
}

/// <summary>
/// Singleton: additive camera shake. DamageFeedbackSystem (or anyone) calls <c>world.ShakeCamera(...)</c>.
/// CameraShakeSystem removes the last offset before FollowSystem (so following sees the clean position)
/// and applies a fresh one at the end of the frame (so rendering sees the shaken one).
/// </summary>
public struct CameraShakeComponent
{
	public Transform Camera;
	public Vector3 AppliedOffset;
	public float Timer;
	public float Duration;
	public float Amplitude;
}

public struct DisposableComponent
{
	public bool IsDisposed;
}

public struct LifeTimeComponent
{
	public float Value;
}

#region Decals
public struct DecalPoolComponent
{
	public List<Decal> Value;
	public Dictionary<string, Stack<Decal>> Pools;
	public Transform Parent;
}

public struct DecalComponent
{
	public Decal Value;
	public float Lifetime;
	public bool IsDisposed;
}

public struct RequestDecalComponent
{
	public string Id;
	public Vector3 Direction;
	public Vector3 Position;
	// Orient the decal strictly along Direction (e.g. along the bullet trajectory), without random rotation.
	public bool AlignToDirection;
}
#endregion

#region Navigation
public struct NavMeshManagerComponent
{
	public NavMeshManager Value;
}

public struct CurrentSectorComponent
{
	public FloorSector Value;
}
#endregion

#region UI
public struct PlayerStatsComponent
{
	public PlayerStats Value;
}
public struct WeaponUIViewComponent
{
	public WeaponUIView Value;
}

public struct RequestOpenWindowComponent
{
	public WindowType WindowType;
}

public struct FailWindowComponent
{
	public FailWindow Value;
}

public struct WinWindowComponent
{
	public WinWindow Value;
}

public struct DifficultyTimerUIComponent
{
	public DifficultyTimerView Value;
}

public struct RequestShowDifficultyComponent
{
	public DifficultyLevel DifficultyLevel;
	public float Seconds;
}

public struct RequestHideDifficultyComponent
{
}

public struct UpdateAmmoViewRequestComponent
{
}

public struct UpdateWeaponViewRequestComponent
{
}

public struct UpdateHealthViewRequestComponent
{

}

public struct RequestUpdateFragCountComponent
{

}

public struct FragCountComponent
{
	public int Value;
}

// Holds the on-screen pickup/notification log view (VerticalLayoutGroup of text slots).
public struct UILogViewComponent
{
	public UILogView Value;
}

// Generic "show this line in the UI log" request. Any system can raise it; UILogSystem dispatches it.
public struct RequestUILogComponent
{
	public string Message;
}
#endregion

public struct DifficultyComponent
{
	public float DifficultyTimer;
	public ISmartCondition[] Conditions;
	public DifficultyStage Stage;
}

public struct InterSpawnCooldownComponent
{
	public float Value;
}

public struct PauseStateComponent
{
	public bool IsPaused;
	public SignalSource PreviousSource;
}

public struct RequestPauseComponent
{
	public SignalSource Source;
}

public struct RequestUnpauseComponent
{
	public SignalSource Source;
}

public struct InputActionsComponent
{
	public InputActionAsset Value;
	public InputActionMap ActionMap;
	public InputAction MoveAction;
	public InputAction FireAction;
	public InputAction MeleeAction;
	public InputAction ReloadAction;
	public InputAction ThrowAction;
	public InputAction InteractAction;
	// Esc / gamepad Start (PauseMenuSystem). Optional: null if the asset has no "Pause" action.
	public InputAction PauseAction;
}

// Singleton: the runtime pause menu view (PauseMenuSystem).
public struct PauseMenuComponent
{
	public PauseMenu Value;
}

public struct RequestReloadComponent
{

}

public struct RequestSpawnBulletComponent
{
	public Vector3 Position;
	public Vector3 Direction;
	public GunConfig GunConfig;
	// Who is firing: Player (WeaponFireSystem) or Enemy (RangedAttackerSystem). default = Player.
	public BulletTeam Team;
}

public struct SmartConditionComponent
{
	public ISmartCondition Value;
}

public struct AimVisualizerComponent
{
	public AimVisualizer Value;
}

public struct VirtualAimCursorComponent
{
	public Vector2 ScreenPosition;
	public Vector2 PrevPosition;
}

public struct AimInputComponent
{
	public Vector2 Value;      // mouse position OR stick vector
	public InputActionReference AimAction; // input Action from new input system
	public bool IsGamepad;
}

public struct AdditionalLootSpawnHolderComponent
{
	// point -> packed loot entity. Packed (not raw int) because the loot entity is
	// deleted on pickup/despawn and its id recycled — a raw int would make pool.Has
	// throw "Cant touch destroyed entity".
	public Dictionary<Transform, Leopotam.EcsLite.EcsPackedEntity> ActivePoints;
	public IEnumerable<AdditionalLootConfig> LootConfigs;
	public List<Transform> LootPointsPool;
	public float CooldownMax;
}

public struct AdditionalLootObserverComponent
{
	public ISmartCondition Condition;
	public PossibleLoot[] PossibleLoot;
	public SpawnProcess Process;
	public Transform ProcessingPoint;
	public float Cooldown;
}

public struct LootSpawnedEventComponent
{
	public RequestSpawnSource Source;
	public int SourceEntity;  // entity that requested loot spawn
	public int LootEntity;    // lootComponent entity
}

// --- Level events (scene-scripted stage-start actions) ---

// Singleton: scene-placed LevelEventTriggers (found by EntryPoint via FindObjectsByType).
// LevelEventSystem.Init builds observer entities from them. See Docs/LevelEventsFeature.md.
public struct LevelEventHolderComponent
{
	public List<LevelEventTrigger> Triggers;
}

/// <summary>
/// Singleton: scene-placed mission objects of the level (gates, keys, zones, stage loot — found by EntryPoint) and
/// the mission state. Driven by MissionSystem; read by GateOpenedCondition / PlayerInZoneCondition.
/// See Docs/KeyLevelFeature.md.
/// </summary>
public struct MissionComponent
{
	public List<LevelGate> Gates;
	public List<LevelKeyPickup> Keys;
	public List<LevelZone> Zones;
	public List<StageLoot> StageLoots;
	public HashSet<string> CollectedKeys;
	public HashSet<string> OpenedGates;
}

// Runtime state of one entry (LevelEventEntry): armed at the start of the required difficulty stage,
// then spawns breakables once the optional smart-conditions are met.
public struct LevelEventObserverComponent
{
	public LevelEventEntry Entry;
	public Transform Origin;              // trigger transform (fallback spawn point)
	public DifficultyLevel Level;         // stage whose start arms the entry
	public ISmartCondition[] Conditions;  // condition copies (armed); null = not armed / no gate
	public bool Armed;
	public bool Fired;
}

// Inertial movement state (Inertia.Step): added lazily to the player / mobs the first time they step on slippery
// ground (TractionModifier). See Docs/HockeyLevelFeature.md.
public struct InertiaComponent
{
	public Vector3 Velocity;      // real planar velocity, m/s
	public Vector3 PrevPosition;  // position before last frame's step (to detect a blocked step)
	public Vector3 LastStep;      // displacement applied last frame
	public float LastDt;
	public bool Sliding;          // last frame was on slippery ground
}

// Wave quota of the current stage (DifficultyStage.MobQuota): mobs left to spawn per mob id. Rebuilt by
// WaveQuota.Sync when the stage changes; SpawnPointSystem spends it, MobsClearedCondition / MissionSystem read it.
public struct WaveQuotaComponent
{
	public DifficultyStage Stage;
	public Dictionary<string, int> Remaining;
	public int RemainingTotal;
}

public struct ModifierOwnerComponent
{
	public int Entity;
	public Transform Transform;
	public List<Modifier> Modifiers;
	public bool ReadyToRemove;
}

public struct TryApplyModifierComponent
{
	public EcsPackedEntity Target;
	public Modifier Modifier;
}

public struct ApplyModifierResponseComponent
{
	public int TargetEntity;
	public Modifier Modifier;
}

#region Bonus
// Request to apply a bonus to the player (raised when Bonus loot is picked up).
public struct RequestApplyBonusComponent
{
	public string ConfigId;
}

// One active player bonus: its modifier + original duration (for bar/timer normalization).
public struct ActiveBonus
{
	public BonusType Type;
	public Modifier Modifier;
	public float TotalDuration;
	// Player-following VFX (parented to the player). BonusSystem owns its lifecycle:
	// reused on a same-type refresh (no duplicates) and returned to the pool on expiry.
	public SceneEffect Effect;
}

// Singleton: list of the player's active bonuses. BonusSystem prunes expired ones and drives the UI.
public struct ActiveBonusesComponent
{
	public List<ActiveBonus> Value;
}
#endregion

public struct RequestMeleeComponent
{
	public int SourceEntity;
	public Vector3 Position;
	public float Delay;
	public MeleeConfig Config;
	public float Rotation;
}

#region Grenade
/// <summary>
/// Singleton: current grenade count of the player + throw charging state.
/// Charge accumulates while Throw is held; range is computed on release.
/// </summary>
public struct GrenadeStateComponent
{
	public int Count;
	public bool IsCharging;
	public float ChargeTime;
	// Config of the current grenade type (set on loot pickup; see GrenadeConfig).
	public GrenadeConfig CurrentConfig;
	// +1 per thrown grenade (GrenadeThrowSystem); PlayerAnimationSystem fires the "Throw" trigger on change.
	public int ThrowCounter;
}

/// <summary>
/// Singleton reference to the grenade counter UI view.
/// </summary>
public struct GrenadeCounterUIComponent
{
	public GrenadeCounter Value;
}

/// <summary>
/// Singleton reference to the throw landing-point visualizer.
/// </summary>
public struct GrenadeAimVisualizerComponent
{
	public GrenadeAimVisualizer Value;
}

/// <summary>
/// Singleton: pool of thrown grenade projectiles.
/// </summary>
public struct GrenadePoolComponent
{
	public Stack<Grenade> Value;
	public Transform Parent;
}

/// <summary>
/// Flying grenade. Moves along a parabolic arc from Start to Target;
/// on landing (Elapsed >= FlightTime) raises a RequestExplosionComponent
/// and returns to the pool. Carries its own explosion params — independent of the config.
/// </summary>
public struct GrenadeProjectileComponent
{
	public Grenade Value;
	public Vector3 Start;
	public Vector3 Target;
	public float Elapsed;
	public float FlightTime;
	public float ArcHeight;
	// explosion params passed on landing
	public float Radius;
	public float MaxDamage;
	public float MinDamage;
	public float FuseDelay;
	public string EffectId;
	// Share of damage to mobs / to the player (0..1). The explosion hits everyone in radius; scale scales the damage.
	public float MobDamageScale;
	public float PlayerDamageScale;
	// escorting trail effect (child of the grenade); returned to the effect pool on explosion.
	public SceneEffect TrailEffect;
}

/// <summary>
/// Tag-request: refresh the grenade counter display in UISystem.
/// </summary>
public struct UpdateGrenadeViewRequestComponent
{
}

/// <summary>
/// Request: trigger an on-demand explosion at a given point.
/// Delay — fuse (sec): while > 0 the explosion is deferred. Damage falls linearly
/// from MaxDamage (center) to MinDamage (radius edge).
/// </summary>
public struct RequestExplosionComponent
{
	public Vector3 Position;
	public float Radius;
	public float MaxDamage;
	public float MinDamage;
	public float Delay;
	public string EffectId;
	// Share of damage to mobs / to the player (0..1). The explosion hits everyone in radius; 0 = doesn't touch that target.
	public float MobDamageScale;
	public float PlayerDamageScale;
}

/// <summary>
/// Grenadier mob state. Chase — approaches the player; Throw — stands and throws a grenade
/// (animation "throw"); Cooldown — stands reloading (animation "throw_cooldown");
/// Flee — retreats to a free spot when the player is closer than the minimum distance.
/// </summary>
public enum GrenadierState : byte
{
	Chase,
	Throw,
	Cooldown,
	Flee
}

/// <summary>
/// Per-entity: a grenade-throwing mob. Sits on top of the regular MobComponent.
/// Distances/cooldown/grenade type come from GrenadierMobConfig.
/// </summary>
public struct GrenadierComponent
{
	public GrenadierMobConfig Config;
	public GrenadierState State;
	// In Throw — windup countdown until the grenade leaves; in Cooldown — reload countdown.
	public float Timer;
	// Retreat point (on the NavMesh) picked in the Flee state.
	public Vector3 FleeTarget;
	public bool HasFleeTarget;
}
#endregion

#region MeleeAttacker
/// <summary>
/// Melee mob state. Chase — approaches the player (regular pathfinding);
/// Windup — stands and winds up (animation "attack", pre-strike phase); Cooldown — stands
/// in the recovery phase of the same animation until the cooldown expires, then Chase again.
/// </summary>
public enum MeleeAttackerState : byte
{
	Chase,
	Windup,
	Cooldown
}

/// <summary>
/// Per-entity: a mob striking with a telegraphed melee attack (like the player) instead of contact damage.
/// Sits on top of the regular MobComponent. Attack distance comes from MeleeMobConfig, while the
/// strike params (damage/radius/targets/windup/cooldown) come from the embedded MeleeConfig. All three
/// phases (windup → strike → recovery) play in a single "attack" animation. Contact damage is
/// disabled for such mobs in CollisionSystem.
/// </summary>
public struct MeleeAttackerComponent
{
	public MeleeMobConfig Config;
	public MeleeAttackerState State;
	// In Windup — windup countdown until the strike; in Cooldown — recovery countdown.
	public float Timer;
}
#endregion

#region RangedAttacker
public enum RangedAttackerState : byte
{
	Chase,
	Windup,
	Burst,    // between the shots of a burst (RangedMobConfig.BurstCount > 1)
	Cooldown
}

/// <summary>
/// Per-entity: a ranged mob. On top of the regular MobComponent — a telegraphed ranged attack (like the
/// player's: windup → shot → recovery, a single "attack" animation). Combat distance and shot
/// params come from RangedMobConfig (the embedded GunConfig — the same type as the player's weapon).
/// Added by MobSpawnSystem only when MobConfig is RangedMobConfig. Behavior — RangedAttackerSystem.
/// </summary>
public struct RangedAttackerComponent
{
	public RangedMobConfig Config;
	public RangedAttackerState State;
	// In Windup — windup countdown until the shot; in Burst — time to the next shot; in Cooldown — recovery countdown.
	public float Timer;
	// Shots still to fire in the current burst (Burst state).
	public int ShotsLeft;
}

/// <summary>
/// A dead mob's view that still plays its "die" animation. The gameplay entity is already deleted at the
/// moment of death (DamageSystem), so nothing targets/collides with the corpse; this separate entity only keeps
/// the GameObject alive for <see cref="MobConfig.DeathAnimationTime"/> seconds, then DyingMobSystem returns it
/// to the mob pool. Only skinned (non-VAT) mobs with DeathAnimationTime > 0.
/// </summary>
public struct DyingMobComponent
{
	public Mob Value;
	public float Timer;
}
#endregion

#region Formation
/// <summary>
/// Per-entity: a follower mob marching in formation behind the leader. Sits on top of the regular MobComponent.
/// The leader is a regular mob that pathfinds to the player; the follower computes the world
/// position of its "slot" from the leader's position/rotation each frame and steers toward it (FormationSystem,
/// no navmesh — direct movement via MoveComponent.Direction). The leader reference is an
/// EcsPackedEntity, not a Transform: when the leader dies the entity generation changes and Unpack
/// returns false, even if the leader's GameObject was already reused from the pool.
/// </summary>
public struct FormationFollowerComponent
{
	public Leopotam.EcsLite.EcsPackedEntity Leader;
	public Vector3 SlotOffset; // local offset from the leader (x — right, z — forward), computed at spawn
	public bool InFormation;   // hysteresis from §7c: easier to stay in formation than to enter it
}

/// <summary>
/// Group spawn point (squad of mobs in formation). On the point's cooldown GroupSpawnSystem spawns
/// the leader (slot 0) + followers (slots 1..N) at once per GroupSpawnConfig and links them into a
/// formation. Timer — countdown to spawn.
/// </summary>
public struct GroupSpawnPointComponent
{
	public float Timer;
	public GroupSpawnPoint Value;
}
#endregion

#region Breakable
/// <summary>
/// Per-entity: a breakable environment object. Sits on the entity together with HealthComponent (HP) and
/// ColliderComponent (CollisionType.Breakable, for hits). Only sources allowed in <see cref="BreakableConfig"/> deal damage;
/// at HP<=0 BreakableSystem plays effects, scatters loot and applies the outcome (Vanish/Debris), then deletes the entity.
/// ContactCooldown — throttling of mob contact damage (MobContact source).
/// </summary>
public struct BreakableComponent
{
	public Breakable Value;
	public BreakableConfig Config;
	public float ContactCooldown;
	// true — the object was created at runtime from the pool (RequestSpawnBreakable) and returns to the pool on Vanish;
	// false — placed in the scene (scene-placed), on Vanish simply deactivates.
	public bool Pooled;
}

// Singleton: pool of breakable objects by config id (like MobPool/EffectPool).
public struct BreakablePoolComponent
{
	public Dictionary<string, Stack<Breakable>> Pools;
	public Transform Parent;
}

/// <summary>
/// Request: spawn a breakable environment object at a given point. The config is given either directly
/// (Config) or by id (Id, resolved via MainHolder.BreakableConfigHolder). Rotation — Y rotation
/// (degrees). Handled by BreakableSpawnSystem.
/// </summary>
public struct RequestSpawnBreakableComponent
{
	public BreakableConfig Config;
	public string Id;
	public Vector3 Position;
	public float Rotation;
}
#endregion

#region Cheerleaders
public enum CheerleaderState : byte
{
	Dancing,     // on the stage, random dance; swaps the dance after being off screen
	Posing,      // victory: a static pose, each girl her own
	Escaping,    // player died: jumped down, running to the far corner (zombie target)
	Safe,        // reached the escape point (still a zombie target)
	Dead,
	Transformed  // forgotten during a breather: hidden, zombie cheerleaders spawned instead
}

/// <summary>One cheerleader NPC (ECS.CheerleaderSystem). Not a mob: no MobComponent/HealthComponent/collider.</summary>
public struct CheerleaderComponent
{
	public Cheerleader View;
	public CheerleaderStand Stand;
	public int Index;
	public CheerleaderState State;
	public float Health;
	public float HitCooldown;
	public bool WasInView;
}

/// <summary>
/// Marker: a living friendly NPC (cheerleaders now, any "good" NPC later). While at least one exists when the
/// player's death sequence reaches the menu, FailSequenceSystem opens the fail window WITHOUT pausing the world (the
/// NPCs keep fleeing, the horde keeps chasing); the usual full stop follows once none are left. Removed on death /
/// zombification.
/// </summary>
public struct FriendlyNpcComponent
{
}

/// <summary>
/// A non-player target for mobs: after the player's death MobPathfindingSystem sends every mob to the nearest one
/// (escaping cheerleaders). Removed when the target dies.
/// </summary>
public struct ZombieTargetComponent
{
	public Transform Value;
}

/// <summary>
/// Singleton: outcome of the hidden "don't forget the girls" quest (hockey level). Read-only for now — an
/// achievement can hook here later (Saved = victory with the cheerleaders never forgotten).
/// </summary>
public struct CheerleaderQuestComponent
{
	public bool Forgotten;
	public bool Saved;
}
#endregion
