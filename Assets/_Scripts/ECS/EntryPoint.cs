using Leopotam.EcsLite;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ECS
{
	public class EntryPoint : MonoBehaviour
	{
		#region FIELDS
		[Title("Level")]
		[SerializeField, BoxGroup("Level"),
		 Tooltip("Уровень для прямого запуска геймплейной сцены (когда не выбран через меню/GameSession).")]
		private LevelDefinition _fallbackLevel;
		[SerializeField, BoxGroup("Level"),
		 Tooltip("Запасной конфиг, если уровень не разрешён и в сцене нет LevelRoot.")]
		private LevelConfig _levelConfig;

		[Title("�������� ������")]
		[SerializeField, Required, BoxGroup("Game References")] private MainHolder _mainHolder;
		[SerializeField, Required, BoxGroup("Game References")] private Player _player;
		[SerializeField, Required, BoxGroup("Game References")] private Camera _mainCamera;

		[Space]
		[Title("�������� ��������")]
		[SerializeField, Required, BoxGroup("Parents")] private Transform _mobParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _bulletParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _effectParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _decalParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _lootParent;
		[SerializeField, BoxGroup("Parents")] private Transform _grenadeParent;
		[Space]
		[Title("����� ������ �����")]
		[SerializeField, Required, ListDrawerSettings, BoxGroup("Spawn Points")]
		private Transform[] _spawnPoints;
		[Title("����� ������ ��� ����"), SerializeField]
		private Transform[] _additionalLootPoints;
		[Title("UI")]
		[SerializeField, Required, BoxGroup("UI")] private PlayerStats _playerStats;
		[SerializeField, Required, BoxGroup("UI")] private WeaponUIView _weaponView;
		[SerializeField, Required, BoxGroup("UI")] private FailWindow _failWindow;
		[SerializeField, Required, BoxGroup("UI")] private WinWindow _winWindow;
		[SerializeField, Required, BoxGroup("UI")] private DifficultyTimerView _difficultyTimerView;
		[SerializeField, Required, BoxGroup("UI")] private GrenadeCounter _grenadeCounter;
		[SerializeField, Required, BoxGroup("UI")] private UILogView _uiLogView;

		[Title("Input")]
		[SerializeField, Required, BoxGroup("Input")] private InputActionReference _aimAction;
		[SerializeField, Required, BoxGroup("Input")] private InputActionAsset _inputActions;
		// ECS
		private EcsWorld _world;
		private EcsSystems _systems;

		// Level (разрешается в LoadLevel)
		private LevelRoot _levelRoot;
		private LevelConfig _activeLevelConfig;

		#endregion

		#region UNITY EVENTS

		private void Awake()
		{
			_world = new EcsWorld();
			_systems = new EcsSystems(_world);

			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = 60;
			
			LoadLevel();
			SetupSpawnData();
			RegisterSystems();
		}

		private void Update()
		{
			_systems?.Run();
		}

		private void OnDestroy()
		{
			_systems?.Destroy();
			_systems = null;

			_world?.Destroy();
			_world = null;
		}

		#endregion

		#region ECS INITIALIZATION

		[Button(ButtonSizes.Large), DisableInEditorMode]
		private void SetupSpawnData()
		{
			SetUpLevel();

			// --- ����� �������� ���������� ---
			int appEntity = _world.NewEntity();
			ref var config = ref _world.GetPool<MainHolderComponent>().Add(appEntity);
			config.Value = _mainHolder;

			ref var navMeshManager = ref _world.CreateSimpleEntity<NavMeshManagerComponent>();
			navMeshManager.Value = FindFirstObjectByType<NavMeshManager>();
			// Скармливаем секторы из префаба уровня и запекаем navmesh (после инстанса префаба).
			// Если уровень загружен из сцены без LevelRoot — Configure(null) использует ссылки из инспектора.
			navMeshManager.Value.Configure(_levelRoot != null ? _levelRoot.Sectors : null);
			ref var fragCount = ref _world.CreateSimpleEntity<FragCountComponent>();
			fragCount.Value = 0;

			ref var inputActions = ref _world.CreateSimpleEntity<InputActionsComponent>();
			inputActions.Value = _inputActions;

			// --- UI ���������� ---
			ref var weaponViewComponent = ref _world.CreateSimpleEntity<WeaponUIViewComponent>();
			weaponViewComponent.Value = _weaponView;

			ref var playerStatsComponent = ref _world.CreateSimpleEntity<PlayerStatsComponent>();
			playerStatsComponent.Value = _playerStats;

			ref var failWindowComponent = ref _world.CreateSimpleEntity<FailWindowComponent>();
			failWindowComponent.Value = _failWindow;

			ref var winWindowComponent = ref _world.CreateSimpleEntity<WinWindowComponent>();
			winWindowComponent.Value = _winWindow;

			ref var difficultyTimerUIComponent = ref _world.CreateSimpleEntity<DifficultyTimerUIComponent>();
			difficultyTimerUIComponent.Value = _difficultyTimerView;

			ref var uiLogViewComponent = ref _world.CreateSimpleEntity<UILogViewComponent>();
			uiLogViewComponent.Value = _uiLogView;

			// --- ����� ������ ---
			var spawnPointPool = _world.GetPool<SpawnPointComponent>();
			var spawnTimerPool = _world.GetPool<SpawnTimer>();
			var spawnPoints = FindObjectsByType<SpawnPoint>(FindObjectsSortMode.None);
			foreach (var spawnPoint in spawnPoints)
			{
				int spawnEntity = _world.NewEntity();
				ref var sp = ref spawnPointPool.Add(spawnEntity);
				ref var st = ref spawnTimerPool.Add(spawnEntity);
				sp.Value = spawnPoint;
				st.LastSpawnTime = 0;
			}
			ref var spawnPointsComponent = ref _world.CreateSimpleEntity<SpawnPointsComponent>();
			spawnPointsComponent.Value = _spawnPoints;

			// --- ������� ������ ---
			ref var spawnRequest = ref _world.CreateSimpleEntity<SpawnRequestComponent>();
			spawnRequest.MaxCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.MinCoolDown = _mainHolder.MinSpawnCoolDown;
			spawnRequest.CurrentCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.LastSpawnTime = 0;
			spawnRequest.IsBlocked = false;

			// --- ��������� � ���� ---
			ref var soundHolderComponent = ref _world.CreateSimpleEntity<SoundHolderComponent>();
			soundHolderComponent.Value = _mainHolder.SoundHolder;

			ref var effectsHolder = ref _world.CreateSimpleEntity<EffectsHolderComponent>();
			effectsHolder.Value = _mainHolder.EffectsHolder;

			ref var decalsHolder = ref _world.CreateSimpleEntity<DecalsHolderComponent>();
			decalsHolder.Value = _mainHolder.DecalsConfigHolder;

			ref var effectPool = ref _world.CreateSimpleEntity<EffectPoolComponent>();
			effectPool.Value = new();
			effectPool.Pools = new Dictionary<string, Stack<SceneEffect>>();
			effectPool.Parent = _effectParent;

			ref var mobPoolComponent = ref _world.CreateSimpleEntity<MobPoolComponent>();
			mobPoolComponent.Value = new();
			mobPoolComponent.Pools = new Dictionary<string, Stack<Mob>>();
			mobPoolComponent.Parent = _mobParent;

			// Заранее наполняем пул неактивными клонами каждого типа мобов уровня,
			// чтобы Instantiate не давал хитчи во время волн (spawnPoints найдены выше).
			PrewarmMobPool(spawnPoints);

			ref var bulletPool = ref _world.CreateSimpleEntity<BulletPoolComponent>();
			bulletPool.Value = new();
			bulletPool.Parent = _bulletParent;

			ref var decalPool = ref _world.CreateSimpleEntity<DecalPoolComponent>();
			decalPool.Value = new();
			decalPool.Pools = new Dictionary<string, Stack<Decal>>();
			decalPool.Parent = _decalParent;

			ref var lootPool = ref _world.CreateSimpleEntity<LootPoolComponent>();
			lootPool.Value = new();
			lootPool.Parent = _lootParent;

			ref var mapLootPool = ref _world.CreateSimpleEntity<MapLootPoolComponent>();
			mapLootPool.Value = new List<MapLoot>();

			var mapLoots = FindObjectsByType<MapLoot>(FindObjectsSortMode.None);

			if (mapLoots != null && mapLoots.Length > 0)
			{
				
				foreach (var mapLoot in mapLoots)
				{
					Debug.Log($"mapLoot Add loot {mapLoot.name}");
					mapLootPool.Value.Add(mapLoot);
				}
			}

				// --- ����� ---

			int playerEntity = _world.NewEntity();
			ref var playerComponent = ref _world.GetPool<PlayerComponent>().Add(playerEntity);
			playerComponent.Value = _player;
			_player.Initialize(playerEntity);
			ref var playerMovement = ref _world.GetPool<MoveComponent>().Add(playerEntity);
			playerMovement.Speed = _mainHolder.PlayerConfig.Speed;
			playerMovement.Transform = _player.transform;
			ref var healthComponent = ref _world.GetPool<HealthComponent>().Add(playerEntity);
			healthComponent.MaxHealth = _mainHolder.PlayerConfig.MaxHealth;
			healthComponent.CurrentHealth = healthComponent.MaxHealth;
			// Без этого health.TargetType = None, и любой урон по TargetType (ближняя атака мобов
			// через MeleeSpawnSystem) проходит мимо игрока — ContainsFlags(None) всегда false.
			healthComponent.TargetType = TargetType.Player;
			_playerStats.SetHealthValue(healthComponent.CurrentHealth);

			ref var playerModifiers = ref _world.GetPool<ModifierOwnerComponent>().Add(playerEntity);
			playerModifiers.Entity = playerEntity;
			playerModifiers.Modifiers = new();
			playerModifiers.Transform = _player.transform;

			// --- Бонусы (speed/shield) ---
			ref var activeBonuses = ref _world.CreateSimpleEntity<ActiveBonusesComponent>();
			activeBonuses.Value = new List<ActiveBonus>();
			// --- Input
			ref var playerInput = ref _world.GetPool<PlayerInputComponent>().Add(playerEntity);
			ref var aimInput = ref _world.GetPool<AimInputComponent>().Add(playerEntity);
			aimInput.AimAction = _aimAction;

			var aimVisualizer = FindFirstObjectByType<AimVisualizer>();
			ref var aimVisualizerComponent = ref _world.CreateSimpleEntity<AimVisualizerComponent>();
			aimVisualizerComponent.Value = aimVisualizer;

			// --- ������ ---
			int cameraFollowerEntity = _world.NewEntity();
			ref var follower = ref _world.GetPool<FollowerComponent>().Add(cameraFollowerEntity);
			follower.Value = _mainCamera.transform;
			ref var movement = ref _world.GetPool<MoveComponent>().Add(cameraFollowerEntity);
			ref var modifiers = ref _world.GetPool<ModifierOwnerComponent>().Add(cameraFollowerEntity);
			modifiers.Entity = cameraFollowerEntity;
			modifiers.Transform = _mainCamera.transform;
			modifiers.Modifiers = new();

			movement.Speed = _mainHolder.CameraSpeed;
			ref var followTarget = ref _world.GetPool<FollowTarget>().Add(cameraFollowerEntity);
			followTarget = _mainHolder.CameraFollowTarget;
			followTarget.Target = _player.transform;
			followTarget.MatchTargetSpeedIfFar = true;
			followTarget.MatchSpeedDistance = 3f;
			ref var offset = ref _world.GetPool<FollowerOffset>().Add(cameraFollowerEntity);
			offset.Value = _mainCamera.transform.position - _player.transform.position;

			int cameraEntity = _world.NewEntity();
			ref var cameraComponent = ref _world.GetPool<CameraComponent>().Add(cameraEntity);
			cameraComponent.Value = _mainCamera;

			// --- ������/������� ---
			ref var muzzle = ref _world.CreateSimpleEntity<WeaponComponent>();
			muzzle.Weapon = _player.Weapon;
			muzzle.GunConfig = _mainHolder.GunConfigHolder.GetConfig("Pistol");
			muzzle.CurrentMagazineCount = muzzle.GunConfig.MagazineCapacity;
			ref var reloadingComponent = ref _world.GetPool<ReloadingComponent>().Add(playerEntity);
			reloadingComponent.ReloadTime = 0;

			// --- Запас патронов по калибрам: стартовый запас идёт в калибр стартового оружия ---
			ref var ammoInventory = ref _world.CreateSimpleEntity<AmmoInventoryComponent>();
			ammoInventory.Ammo = new Dictionary<Caliber, int>();
			ammoInventory.Add(muzzle.GunConfig.Caliber, _mainHolder.StartAmmo);

			_weaponView.SetWeaponView(muzzle.GunConfig, ammoInventory.Get(muzzle.GunConfig.Caliber));

			// --- Grenades ---
			ref var grenadeState = ref _world.CreateSimpleEntity<GrenadeStateComponent>();
			grenadeState.Count = _mainHolder.PlayerConfig.StartGrenades;
			grenadeState.IsCharging = false;
			grenadeState.ChargeTime = 0f;
			var grenadeConfigHolder = _mainHolder.GrenadeConfigHolder;
			if (grenadeConfigHolder != null)
			{
				var startId = _mainHolder.PlayerConfig.StartGrenadeId;
				grenadeState.CurrentConfig = string.IsNullOrEmpty(startId)
					? grenadeConfigHolder.Default
					: grenadeConfigHolder.GetConfig(startId);
			}

			ref var grenadeCounterComponent = ref _world.CreateSimpleEntity<GrenadeCounterUIComponent>();
			grenadeCounterComponent.Value = _grenadeCounter;
			if (_grenadeCounter != null)
				_grenadeCounter.SetCount(grenadeState.Count);

			ref var grenadePool = ref _world.CreateSimpleEntity<GrenadePoolComponent>();
			grenadePool.Value = new();
			// Гранаты всегда складываем под выделенный родитель: если он не назначен в сцене,
			// создаём его в рантайме, чтобы снаряды не плодились в корне иерархии.
			if (_grenadeParent == null)
				_grenadeParent = new GameObject("Grenades").transform;
			grenadePool.Parent = _grenadeParent;

			var grenadeAimVisualizer = FindFirstObjectByType<GrenadeAimVisualizer>();
			ref var grenadeAimComponent = ref _world.CreateSimpleEntity<GrenadeAimVisualizerComponent>();
			grenadeAimComponent.Value = grenadeAimVisualizer;
			if (grenadeAimVisualizer != null)
				grenadeAimVisualizer.Hide();


			// --- ������� LookAtCursor ---
			ref var lookAtCursor = ref _world.CreateSimpleEntity<LookAtCursor>();
			lookAtCursor.Transform = _player.transform;
			lookAtCursor.Mode3D = true;

			// --- ����� ��� ��������� ���� ---
			var drawer = FindFirstObjectByType<PathGizmoDrawer>();
			if (drawer != null)
				drawer.Initialize(_world);

			var additionalSpawnTransforms = GameObject.FindGameObjectsWithTag("AdditionalSpawn")
			   .Select(go => go.transform);

			Dictionary<Transform, Leopotam.EcsLite.EcsPackedEntity> dictionary = new();
			List<Transform> lootPointsPool = new(additionalSpawnTransforms);

			// -- additional loot --
			ref var additionalLootSpawnComponent = ref _world.CreateSimpleEntity<AdditionalLootSpawnHolderComponent>();
			additionalLootSpawnComponent.ActivePoints = dictionary;
			additionalLootSpawnComponent.LootPointsPool = lootPointsPool;
			additionalLootSpawnComponent.LootConfigs = _activeLevelConfig.GetAdditionalLootConfigs();
		}

		// Разрешает выбранный уровень и инстанциирует его префаб ДО SetupSpawnData,
		// чтобы Find*-сканы (SpawnPoint / MapLoot / тег AdditionalSpawn) подхватили его контент.
		// Заранее создаёт неактивные клоны каждого типа мобов, который может появиться
		// на уровне (по MobId всех SpawnPoint'ов), и кладёт их в пул. Так первые волны
		// берут мобов через Pop() вместо Instantiate и не дают хитчей.
		// Кол-во на тип берётся из MainHolder.MobPrewarmPerType (0 = выключено),
		// и не превышает ActiveMobLimit — больше мобов одновременно всё равно не живёт.
		private void PrewarmMobPool(SpawnPoint[] spawnPoints)
		{
			int perType = _mainHolder.MobPrewarmPerType;
			if (perType <= 0 || spawnPoints == null || _mainHolder.MobConfigHolder == null)
				return;

			perType = Mathf.Min(perType, _mainHolder.ActiveMobLimit);
			if (perType <= 0)
				return;

			ref var mobPool = ref _world.GetAsSingleton<MobPoolComponent>();
			mobPool.Pools ??= new Dictionary<string, Stack<Mob>>();

			var warmed = new HashSet<string>();
			foreach (var spawnPoint in spawnPoints)
			{
				var configs = spawnPoint != null ? spawnPoint.SpawnConfigs : null;
				if (configs == null)
					continue;

				foreach (var spawnConfig in configs)
				{
					var id = spawnConfig.MobId;
					// Каждый тип греем один раз, даже если он встречается на нескольких точках.
					if (string.IsNullOrEmpty(id) || !warmed.Add(id))
						continue;

					var mobConfig = _mainHolder.MobConfigHolder.GetConfigById(id);
					if (mobConfig == null || mobConfig.Prefab == null)
						continue;

					if (!mobPool.Pools.TryGetValue(id, out var stack))
					{
						stack = new Stack<Mob>();
						mobPool.Pools[id] = stack;
					}

					for (int i = stack.Count; i < perType; i++)
					{
						var mob = Object.Instantiate(mobConfig.Prefab, mobPool.Parent);
						mob.SetId(id);
						mob.gameObject.SetActive(false);
						stack.Push(mob);
					}
				}
			}
		}

		private void LoadLevel()
		{
			var level = GameSession.SelectedLevel != null ? GameSession.SelectedLevel : _fallbackLevel;

			if (level != null && level.LevelPrefab != null)
			{
				var instance = Instantiate(level.LevelPrefab);
				_levelRoot = instance.GetComponentInChildren<LevelRoot>(true);
				if (_levelRoot == null)
					Debug.LogError($"[EntryPoint] Префаб уровня '{level.LevelPrefab.name}' без LevelRoot.");
			}
			else
			{
				// Прямой запуск геймплейной сцены без выбора уровня: контент уже лежит в сцене.
				_levelRoot = FindFirstObjectByType<LevelRoot>(FindObjectsInactive.Include);
			}

			_activeLevelConfig = _levelRoot != null && _levelRoot.LevelConfig != null
				? _levelRoot.LevelConfig
				: _levelConfig;

			if (_activeLevelConfig == null)
				Debug.LogError("[EntryPoint] Не удалось разрешить LevelConfig (нет LevelRoot и нет запасного конфига).");
		}

		private void SetUpLevel()
		{
			ref var config = ref _world.CreateSimpleEntity<CurrentLevelConfigComponent>();
			config.Value = _activeLevelConfig;
		}

		private void RegisterSystems()
		{
			#region RegisterSystems
			_systems
				.Add(new CheckSectorSystem())
				.Add(new DifficultySystem())
				.Add(new SpawnPointSystem())
				.Add(new AdditionalLootSpawnSystem())
				// Mob systems
				.Add(new MobSpawnSystem())
				// Move and navigation systems
				.Add(new MobPathfindingSystem())
				.Add(new GrenadierSystem())
				.Add(new MeleeAttackerSystem())
				.Add(new MoveSystem())
				.Add(new FollowSystem())
				.Add(new LookAtCameraSystem())
				.Add(new LookAtCursorSystem())
			
				.Add(new AnimationSystem())
				.Add(new ModifiersSystem())

				// Fire and Reload Systems
				.Add(new WeaponFireSystem())
				.Add(new WeaponReloadSystem())
				.Add(new MeleeSpawnSystem())
				.Add(new BulletSystem())
				.Add(new BulletOverlapSystem())
				// Collision and Damage Systems
				.Add(new CollisionSystem())
				.Add(new GrenadeProjectileSystem())
				.Add(new ExplosionSystem())
				.Add(new DamageSystem())
				.Add(new BonusSystem())
				// Other spawning systems
				.Add(new LootSystem())
				.Add(new EffectsSystem())
				.Add(new DecalSystem())
				// Player systems
				.Add(new InputSystem())
				.Add(new GrenadeThrowSystem())
				.Add(new PlayerSystem())
				.Add(new PlayerMovementSystem())
				.Add(new CheckEndSystem())
				.Add(new FailSequenceSystem())
				.Add(new SmartConditionSystem())
				.Add(new AimVisualizerSystem())
				.Add(new UISystem())
				.Add(new UILogSystem())
				.Add(new PauseSystem())
				.Init();
			#endregion
		}

		#endregion
	}
}
