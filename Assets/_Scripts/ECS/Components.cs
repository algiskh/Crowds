using System;
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
	public Stack<Bullet> Value;
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
/// Tag-signal: требует внеочередного пересчёта пути (в обход Interval).
/// Выставляется извне (смена препятствий, динамические блокеры и т.п.),
/// снимается в MobPathfindingSystem после обработки.
/// </summary>
public struct PathRecalculationRequest { }

public struct HealthComponent
{
	public float CurrentHealth;
	public float MaxHealth;
	public TargetType TargetType;
}

public struct RequestDamageComponent
{
	public float Damage;
	public int TargetEntity;
	public IEnumerable<Modifier> DamageModifiers;
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
}

public struct WeaponComponent
{
	public Weapon Weapon;
	public GunConfig GunConfig;
	public float CoolDown;
	public bool IsFiring;
	public int CurrentMagazineCount;
	public float ReloadTime;
}

// Запас патронов игрока по калибрам. Оружие с одинаковым калибром делит один пул.
// Магазин (CurrentMagazineCount) хранится в WeaponComponent, резерв — здесь.
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
/// Кто выпустил пулю — определяет, кого она может ранить. Player-пули бьют мобов/разрушаемое
/// окружение (как раньше), Enemy-пули (мобы-стрелки, RangedAttackerSystem) — игрока.
/// default = Player: старый путь стрельбы игрока остаётся корректным без явной установки.
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
	public FixedList32Bytes<int> PiercedTargets;
}

public struct BulletOverlapComponent
{
	/// <summary>
	/// Mob entity-ids, попавшие в overlap текущего кадра.
	/// Заполняется в BulletOverlapSystem, читается в CollisionSystem.
	/// Никаких managed-аллокаций: FixedList — inline value-type.
	/// </summary>
	public Unity.Collections.FixedList128Bytes<int> MobHits;

	/// <summary>
	/// Breakable entity-ids hit this frame (destructible environment). Filled in BulletOverlapSystem,
	/// consumed in CollisionSystem (BulletVsBreakable). Inline value-type, no managed allocations.
	/// </summary>
	public Unity.Collections.FixedList128Bytes<int> BreakableHits;

	/// <summary>
	/// True если Enemy-пуля дотянулась до игрока в этом кадре (единственная цель — проверяется
	/// по дистанции, а не по коллайдер-карте). Заполняется в BulletOverlapSystem, читается в
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
	// if effect is associated with modifier
	public int ModifierEntity;
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
	public int ModifierEntity;
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
/// Фазы кинематографичной концовки при гибели игрока. Каждая длится FailSequenceSystem.PhaseDuration (0.5с):
///  BlockControls — заблокировано только управление игроком (мир продолжает жить);
///  RedScreen — поверх экрана плавно проявляется красная пелена;
///  Menu — игра ставится на паузу и показывается окно поражения.
/// Done — последовательность завершена.
/// </summary>
public enum FailSequencePhase : byte
{
	Inactive,
	BlockControls,
	RedScreen,
	Menu,
	Done
}

/// <summary>Singleton: состояние кинематографичной концовки (см. FailSequenceSystem).</summary>
public struct FailSequenceComponent
{
	public FailSequencePhase Phase;
	public float Timer;
}

/// <summary>Singleton-обёртка над рантайм-оверлеем красной пелены (создаётся лениво).</summary>
public struct FailScreenOverlayComponent
{
	public FailScreenOverlay Value;
}

/// <summary>
/// Singleton: блокировка пользовательского ввода игрока отдельно от общей паузы. Пока Locked=true,
/// InputSystem/GrenadeThrowSystem игнорируют ввод, при этом остальной мир может продолжать жить
/// (используется в фазах BlockControls/RedScreen концовки).
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
	// Ориентировать декаль строго по Direction (напр. по траектории пули), без случайного разворота.
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
}

public struct RequestReloadComponent
{

}

public struct RequestSpawnBulletComponent
{
	public Vector3 Position;
	public Vector3 Direction;
	public GunConfig GunConfig;
	// Кто стреляет: Player (WeaponFireSystem) или Enemy (RangedAttackerSystem). default = Player.
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

// Singleton: разложенные в сцене LevelEventTrigger'ы (найдены EntryPoint через FindObjectsByType).
// LevelEventSystem.Init строит из них observer-сущности. См. Docs/LevelEventsFeature.md.
public struct LevelEventHolderComponent
{
	public List<LevelEventTrigger> Triggers;
}

// Рантайм-состояние одной записи (LevelEventEntry): вооружается на старте нужного стейджа сложности,
// затем при выполнении опциональных smart-условий выполняет спаун breakable'ов.
public struct LevelEventObserverComponent
{
	public LevelEventEntry Entry;
	public Transform Origin;              // трансформ триггера (фолбэк-точка спауна)
	public DifficultyLevel Level;         // стейдж, на старте которого запись вооружается
	public ISmartCondition[] Conditions;  // инстансы-копии условий (вооружены); null = не вооружено / без гейта
	public bool Armed;
	public bool Fired;
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
	public int TargetEntity;
	public Modifier Modifier;
}

public struct ApplyModifierResponseComponent
{
	public int TargetEntity;
	public Modifier Modifier;
}

#region Bonus
// Запрос на применение бонуса игроку (создаётся при подборе Bonus-лута).
public struct RequestApplyBonusComponent
{
	public string ConfigId;
}

// Один активный бонус игрока: его модификатор + изначальная длительность (для нормализации бара/таймера).
public struct ActiveBonus
{
	public BonusType Type;
	public Modifier Modifier;
	public float TotalDuration;
	// Сопровождающий игрока VFX (запарентен к игроку). BonusSystem владеет его жизненным циклом:
	// переиспользует при рефреше того же типа (без дублей) и возвращает в пул при истечении.
	public SceneEffect Effect;
}

// Singleton: список активных бонусов игрока. BonusSystem прунит протухшие и гонит UI.
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
/// Singleton: текущее число гранат у игрока + состояние зарядки броска.
/// Зарядка копится, пока зажат Throw; на отпускании рассчитывается дальность.
/// </summary>
public struct GrenadeStateComponent
{
	public int Count;
	public bool IsCharging;
	public float ChargeTime;
	// Конфиг текущего типа гранаты (задаётся при подборе лута; см. GrenadeConfig).
	public GrenadeConfig CurrentConfig;
}

/// <summary>
/// Singleton-ссылка на UI-вью счётчика гранат.
/// </summary>
public struct GrenadeCounterUIComponent
{
	public GrenadeCounter Value;
}

/// <summary>
/// Singleton-ссылка на визуализатор точки приземления броска.
/// </summary>
public struct GrenadeAimVisualizerComponent
{
	public GrenadeAimVisualizer Value;
}

/// <summary>
/// Singleton: пул брошенных гранат-снарядов.
/// </summary>
public struct GrenadePoolComponent
{
	public Stack<Grenade> Value;
	public Transform Parent;
}

/// <summary>
/// Летящая граната. Движется по параболической дуге от Start к Target;
/// при приземлении (Elapsed >= FlightTime) порождает RequestExplosionComponent
/// и возвращается в пул. Параметры взрыва несёт сама — независимо от конфига.
/// </summary>
public struct GrenadeProjectileComponent
{
	public Grenade Value;
	public Vector3 Start;
	public Vector3 Target;
	public float Elapsed;
	public float FlightTime;
	public float ArcHeight;
	// параметры взрыва, передаваемые при приземлении
	public float Radius;
	public float MaxDamage;
	public float MinDamage;
	public float FuseDelay;
	public string EffectId;
	// Доля урона по мобам / по игроку (0..1). Взрыв задевает всех в радиусе, scale масштабирует урон.
	public float MobDamageScale;
	public float PlayerDamageScale;
	// сопровождающий эффект-трейл (ребёнок гранаты); при взрыве возвращается в пул эффектов.
	public SceneEffect TrailEffect;
}

/// <summary>
/// Tag-request: обновить отображение счётчика гранат в UISystem.
/// </summary>
public struct UpdateGrenadeViewRequestComponent
{
}

/// <summary>
/// Request: устроить взрыв по требованию в заданной точке.
/// Delay — фитиль (сек): пока > 0, взрыв откладывается. Урон линейно
/// падает от MaxDamage (центр) до MinDamage (край радиуса).
/// </summary>
public struct RequestExplosionComponent
{
	public Vector3 Position;
	public float Radius;
	public float MaxDamage;
	public float MinDamage;
	public float Delay;
	public string EffectId;
	// Доля урона по мобам / по игроку (0..1). Взрыв бьёт всех в радиусе; 0 = эту цель не задевает.
	public float MobDamageScale;
	public float PlayerDamageScale;
}

/// <summary>
/// Состояние моба-гренадёра. Chase — подходит к игроку; Throw — стоит и кидает гранату
/// (анимация "throw"); Cooldown — стоит на перезарядке (анимация "throw_cooldown");
/// Flee — отходит на свободное место, когда игрок ближе минимальной дистанции.
/// </summary>
public enum GrenadierState : byte
{
	Chase,
	Throw,
	Cooldown,
	Flee
}

/// <summary>
/// Per-entity: моб, кидающий гранаты. Висит поверх обычного MobComponent.
/// Дистанции/кулдаун/тип гранаты берутся из GrenadierMobConfig.
/// </summary>
public struct GrenadierComponent
{
	public GrenadierMobConfig Config;
	public GrenadierState State;
	// В Throw — отсчёт замаха до вылета гранаты; в Cooldown — отсчёт перезарядки.
	public float Timer;
	// Точка отхода (на NavMesh), выбранная в состоянии Flee.
	public Vector3 FleeTarget;
	public bool HasFleeTarget;
}
#endregion

#region MeleeAttacker
/// <summary>
/// Состояние моба ближнего боя. Chase — подходит к игроку (обычный патфайндинг);
/// Windup — стоит и замахивается (анимация "attack", фаза до удара); Cooldown — стоит
/// в фазе восстановления той же анимации, пока не истечёт кулдаун, затем снова Chase.
/// </summary>
public enum MeleeAttackerState : byte
{
	Chase,
	Windup,
	Cooldown
}

/// <summary>
/// Per-entity: моб, бьющий телеграфированной ближней атакой (как игрок), а не контактным уроном.
/// Висит поверх обычного MobComponent. Дистанция атаки берётся из MeleeMobConfig, а параметры
/// самого удара (урон/радиус/цель/замах/кулдаун) — из вложенного MeleeConfig. Все три фазы
/// (замах → удар → восстановление) проигрываются одной анимацией "attack". Контактный урон для
/// таких мобов отключён в CollisionSystem.
/// </summary>
public struct MeleeAttackerComponent
{
	public MeleeMobConfig Config;
	public MeleeAttackerState State;
	// В Windup — отсчёт замаха до удара; в Cooldown — отсчёт восстановления.
	public float Timer;
}
#endregion

#region RangedAttacker
public enum RangedAttackerState : byte
{
	Chase,
	Windup,
	Cooldown
}

/// <summary>
/// Per-entity: моб-стрелок. Поверх обычного MobComponent — телеграфированная дальняя атака (как у
/// игрока: замах → выстрел → восстановление, одна анимация "attack"). Дистанция боя и параметры
/// выстрела берутся из RangedMobConfig (вложенный GunConfig — тот же тип, что у оружия игрока).
/// Добавляется MobSpawnSystem только когда MobConfig is RangedMobConfig. Поведение — RangedAttackerSystem.
/// </summary>
public struct RangedAttackerComponent
{
	public RangedMobConfig Config;
	public RangedAttackerState State;
	// В Windup — отсчёт замаха до выстрела; в Cooldown — отсчёт восстановления.
	public float Timer;
}
#endregion

#region Formation
/// <summary>
/// Per-entity: моб-ведомый, идущий в строю за ведущим. Висит поверх обычного MobComponent.
/// Ведущий — обычный моб, который патфайндит к игроку; ведомый каждый кадр считает мировую
/// позицию своего «слота» от позиции/поворота ведущего и рулит к ней (FormationSystem,
/// без navmesh — прямое движение через MoveComponent.Direction). Ссылка на ведущего —
/// EcsPackedEntity, а не Transform: при гибели ведущего поколение сущности меняется и Unpack
/// вернёт false, даже если GameObject ведущего успели переиспользовать из пула.
/// </summary>
public struct FormationFollowerComponent
{
	public Leopotam.EcsLite.EcsPackedEntity Leader;
	public Vector3 SlotOffset; // локальный офсет от ведущего (x — вправо, z — вперёд), посчитан при спауне
	public bool InFormation;   // гистерезис из §7c: попал в слот — держаться легче
}

/// <summary>
/// Точка спауна группы (отряда) мобов в строю. По кулдауну GroupSpawnSystem разом спаунит
/// ведущего + ведомых по GroupSpawnConfig и связывает их в строй. Таймер — отсчёт до спауна.
/// </summary>
public struct GroupSpawnPointComponent
{
	public float Timer;
	public GroupSpawnPoint Value;
}
#endregion

#region Breakable
/// <summary>
/// Per-entity: разрушаемый объект окружения. Висит на сущности вместе с HealthComponent (HP) и
/// ColliderComponent (CollisionType.Breakable, для попаданий). Урон принимают только источники,
/// разрешённые в <see cref="BreakableConfig"/>; при HP<=0 BreakableSystem проигрывает эффекты,
/// сыплет лут и применяет исход (Vanish/Debris), после чего удаляет сущность.
/// ContactCooldown — троттлинг урона от контакта мобов (источник MobContact).
/// </summary>
public struct BreakableComponent
{
	public Breakable Value;
	public BreakableConfig Config;
	public float ContactCooldown;
	// true — объект создан в рантайме из пула (RequestSpawnBreakable) и при Vanish возвращается в пул;
	// false — расставлен в сцене (scene-placed), при Vanish просто деактивируется.
	public bool Pooled;
}

// Singleton: пул разрушаемых объектов по id конфига (как MobPool/EffectPool).
public struct BreakablePoolComponent
{
	public Dictionary<string, Stack<Breakable>> Pools;
	public Transform Parent;
}

/// <summary>
/// Request: заспаунить разрушаемый объект окружения в заданной точке. Конфиг задаётся либо напрямую
/// (Config), либо по id (Id, резолвится через MainHolder.BreakableConfigHolder). Rotation — поворот
/// вокруг Y (град). Обрабатывается BreakableSpawnSystem.
/// </summary>
public struct RequestSpawnBreakableComponent
{
	public BreakableConfig Config;
	public string Id;
	public Vector3 Position;
	public float Rotation;
}
#endregion