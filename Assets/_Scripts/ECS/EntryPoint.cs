using Cysharp.Threading.Tasks;
using Leopotam.EcsLite;
using Sirenix.OdinInspector;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ECS
{
	public class EntryPoint : MonoBehaviour
	{
		#region FIELDS
		[Title("Level")]
		[SerializeField, BoxGroup("Level"),
			Tooltip("Level for launching the gameplay scene directly (when not chosen via menu/GameSession).")]
		private LevelDefinition _fallbackLevel;
		[SerializeField, BoxGroup("Level"),
			Tooltip("Fallback config when the level can't be resolved and there's no LevelRoot in the scene.")]
		private LevelConfig _levelConfig;

		[Title("Game References")]
		[SerializeField, Required, BoxGroup("Game References")] private MainHolder _mainHolder;
		[SerializeField, Required, BoxGroup("Game References")] private Player _player;
		[SerializeField, Required, BoxGroup("Game References")] private Camera _mainCamera;

		[Space]
		[Title("Parents")]
		[SerializeField, Required, BoxGroup("Parents")] private Transform _mobParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _bulletParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _effectParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _decalParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _lootParent;
		[SerializeField, BoxGroup("Parents")] private Transform _grenadeParent;
		[SerializeField, BoxGroup("Parents")] private Transform _breakableParent;
		[Space]
		[Title("Mob Spawn Points")]
		[SerializeField, Required, ListDrawerSettings, BoxGroup("Spawn Points")]
		private Transform[] _spawnPoints;
		[Title("Additional Loot Points"), SerializeField]
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

		// Level (resolved in LoadLevel)
		private LevelRoot _levelRoot;
		private LevelConfig _activeLevelConfig;

		// Level NavMesh manager (cached in SetupSpawnData for async baking).
		private NavMeshManager _navMeshManager;
		// While false — the level is still loading, systems don't run (see Update).
		private bool _initialized;

		#endregion

		#region UNITY EVENTS

		private void Awake()
		{
			_world = new EcsWorld();
			_systems = new EcsSystems(_world);

			QualitySettings.vSyncCount = 0;
			Application.targetFrameRate = 60;

			// The loading curtain is usually already shown from the menu/restart window before the scene change.
			// For a direct launch of the gameplay scene (from the editor) we show it here —
			// the call is idempotent.
			LoadingScreen.Show();

			// We spread the heavy initialization across frames so the curtain renders and animates,
			// and the biggest hitch (navmesh baking) goes into an async operation.
			InitializeAsync(this.GetCancellationTokenOnDestroy()).Forget();
		}

		private void Update()
		{
			if (!_initialized)
				return;

			_systems?.Run();
		}

		// Step-by-step async level boot. Between steps we yield a frame so the curtain updates;
		// navmesh is baked asynchronously; the curtain is lifted only after the camera renders
		// the first real frame of the level.
		private async UniTaskVoid InitializeAsync(CancellationToken token)
		{
			bool completed = false;
			try
			{
				// 1) Level prefab instance (all scene content must exist before the Find* scans).
				LoadLevel();
				LoadingScreen.SetProgress(0.2f);
				await UniTask.Yield(PlayerLoopTiming.Update, token);

				// 2) Scene data: pools, player, UI, spawn points. NavMesh is NOT baked here (bake:false).
				SetupSpawnData();
				LoadingScreen.SetProgress(0.5f);
				await UniTask.Yield(PlayerLoopTiming.Update, token);

				// 3) Async navmesh baking — doesn't block the frame.
				if (_navMeshManager != null)
					await _navMeshManager.RebuildNavMeshAsync().ToUniTask(cancellationToken: token);
				// The player's agent was enabled before the navmesh existed — bind it to the fresh mesh.
				var playerAgent = _player.GetComponent<UnityEngine.AI.NavMeshAgent>();
				// Mob-only navmesh (dark spawn tunnels, see HockeyLevelBuilder): mobs walk out, the player can't walk in.
				int mobOnlyArea = UnityEngine.AI.NavMesh.GetAreaFromName("MobOnly");
				if (playerAgent != null && mobOnlyArea >= 0)
					playerAgent.areaMask &= ~(1 << mobOnlyArea);
				if (playerAgent != null && playerAgent.isActiveAndEnabled)
					playerAgent.Warp(_player.transform.position);
				LoadingScreen.SetProgress(0.7f);

				// 3b) Pool + shader warm-up under the curtain (first explosion/decal/bullet won't hitch).
				await PoolPrewarmer.RunAsync(_world, _mainHolder, _player.transform.position, token);
				LoadingScreen.SetProgress(0.8f);

				// 4) Register systems and allow them to run.
				RegisterSystems();
				_initialized = true;
				LoadingScreen.SetProgress(1f);

				// 5) Give a frame for the systems to work (camera positioning, etc.) and wait for the end of
				//    the frame — the level is already rendered by the camera. Then smoothly lift the curtain.
				await UniTask.Yield(PlayerLoopTiming.Update, token);
				await UniTask.WaitForEndOfFrame(this, token);
				completed = true;
				LoadingScreen.HideAndDestroy();
			}
			catch (System.Exception e) when (!(e is System.OperationCanceledException))
			{
				Debug.LogException(e, this);
			}
			finally
			{
				// Boot failed — lift the curtain (it's DontDestroyOnLoad and blocks input), otherwise the game hangs
				// on the loading screen. On cancellation (scene unloaded/restart) we don't touch it: the curtain is
				// already needed by the next scene.
				if (!completed && !token.IsCancellationRequested)
					LoadingScreen.HideAndDestroy();
			}
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
			PlacePlayerAtLevelStart();

			// --- App config ---
			int appEntity = _world.NewEntity();
			ref var config = ref _world.GetPool<MainHolderComponent>().Add(appEntity);
			config.Value = _mainHolder;

			ref var navMeshManager = ref _world.CreateSimpleEntity<NavMeshManagerComponent>();
			navMeshManager.Value = FindFirstObjectByType<NavMeshManager>();
			// Feed the sectors from the level prefab, but do NOT bake navmesh here: the bake is big
			// and goes into an async operation (InitializeAsync → RebuildNavMeshAsync), so as not to
			// hang the frame under the loading curtain.
			// If the level was loaded from a scene without LevelRoot — Configure(null) uses the inspector references.
			navMeshManager.Value.Configure(_levelRoot != null ? _levelRoot.Sectors : null, bake: false);
			_navMeshManager = navMeshManager.Value;
			ref var fragCount = ref _world.CreateSimpleEntity<FragCountComponent>();
			fragCount.Value = 0;

			ref var inputActions = ref _world.CreateSimpleEntity<InputActionsComponent>();
			inputActions.Value = _inputActions;
			// Apply the player's custom bindings (from control settings) before reading input.
			Game.Settings.ControlSettings.Apply(_inputActions);

			// --- UI components ---
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

			// --- Spawn points ---
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

			// --- Formation squad spawn points (GroupSpawnPoint) ---
			var groupSpawnPointPool = _world.GetPool<GroupSpawnPointComponent>();
			var groupSpawnPoints = FindObjectsByType<GroupSpawnPoint>(FindObjectsSortMode.None);
			foreach (var groupSpawnPoint in groupSpawnPoints)
			{
				int groupEntity = _world.NewEntity();
				ref var gsp = ref groupSpawnPointPool.Add(groupEntity);
				gsp.Value = groupSpawnPoint;
				gsp.Timer = groupSpawnPoint.Config != null ? groupSpawnPoint.Config.InitialDelay : 0f;
			}

			// --- Spawn requests ---
			ref var spawnRequest = ref _world.CreateSimpleEntity<SpawnRequestComponent>();
			spawnRequest.MaxCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.MinCoolDown = _mainHolder.MinSpawnCoolDown;
			spawnRequest.CurrentCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.LastSpawnTime = 0;
			spawnRequest.IsBlocked = false;

			// --- Sounds and effects ---
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

			// Prewarm the pool with inactive clones of each mob type on the level,
			// so Instantiate doesn't hitch during waves (spawnPoints found above).
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

			// --- Breakable environment ---
			// Runtime object pool (RequestSpawnBreakable puts/takes instances by config id).
			// If the parent isn't assigned in the scene — create it at runtime so instances don't pile up at the root.
			if (_breakableParent == null)
				_breakableParent = new GameObject("Breakables").transform;
			ref var breakablePoolComponent = ref _world.CreateSimpleEntity<BreakablePoolComponent>();
			breakablePoolComponent.Pools = new Dictionary<string, Stack<Breakable>>();
			breakablePoolComponent.Parent = _breakableParent;

			// Scene objects (already placed in the level prefab, found like SpawnPoint/MapLoot).
			// We register their ECS state the same way as runtime spawn — RegisterBreakable(pooled:false).
			var breakables = FindObjectsByType<Breakable>(FindObjectsSortMode.None);
			foreach (var breakable in breakables)
			{
				if (breakable == null || breakable.Config == null || breakable.Collider == null)
				{
					if (breakable != null)
						Debug.LogWarning($"[EntryPoint] Breakable '{breakable.name}' has no Config/Collider — skipped.");
					continue;
				}

				BreakableSpawnSystem.RegisterBreakable(_world, breakable, breakable.Config, pooled: false);
			}

			// Scene level script-events (empty objects with an entry list). LevelEventSystem.Init
			// will build observers from them. We always create the holder — even an empty one (the system no-ops itself).
			var levelEventTriggers = FindObjectsByType<LevelEventTrigger>(FindObjectsSortMode.None);
			ref var levelEventHolder = ref _world.CreateSimpleEntity<LevelEventHolderComponent>();
			levelEventHolder.Triggers = new List<LevelEventTrigger>(levelEventTriggers);

			RegisterMissionObjects();

				// --- Player ---

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
			// Without this health.TargetType = None, and any damage checked by TargetType (mob melee attack
			// via MeleeSpawnSystem) passes the player by — ContainsFlags(None) is always false.
			healthComponent.TargetType = TargetType.Player;
			_playerStats.SetHealthValue(healthComponent.CurrentHealth);

			ref var playerModifiers = ref _world.GetPool<ModifierOwnerComponent>().Add(playerEntity);
			playerModifiers.Entity = playerEntity;
			playerModifiers.Modifiers = new();
			playerModifiers.Transform = _player.transform;

			// --- Bonuses (speed/shield) ---
			ref var activeBonuses = ref _world.CreateSimpleEntity<ActiveBonusesComponent>();
			activeBonuses.Value = new List<ActiveBonus>();
			// --- Input
			ref var playerInput = ref _world.GetPool<PlayerInputComponent>().Add(playerEntity);
			ref var aimInput = ref _world.GetPool<AimInputComponent>().Add(playerEntity);
			aimInput.AimAction = _aimAction;

			var aimVisualizer = FindFirstObjectByType<AimVisualizer>();
			ref var aimVisualizerComponent = ref _world.CreateSimpleEntity<AimVisualizerComponent>();
			aimVisualizerComponent.Value = aimVisualizer;

			// --- Camera ---
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

			// --- Weapon/muzzle ---
			ref var muzzle = ref _world.CreateSimpleEntity<WeaponComponent>();
			muzzle.Weapon = _player.Weapon;
			muzzle.GunConfig = _mainHolder.GunConfigHolder.GetConfig("Pistol");
			muzzle.CurrentMagazineCount = muzzle.GunConfig.MagazineCapacity;
			ref var reloadingComponent = ref _world.GetPool<ReloadingComponent>().Add(playerEntity);
			reloadingComponent.ReloadTime = 0;

			// --- Ammo reserve per caliber: the starting reserve goes into the starting weapon's caliber ---
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
			// Grenades always go under a dedicated parent: if it isn't assigned in the scene,
			// we create it at runtime so projectiles don't pile up at the hierarchy root.
			if (_grenadeParent == null)
				_grenadeParent = new GameObject("Grenades").transform;
			grenadePool.Parent = _grenadeParent;

			var grenadeAimVisualizer = FindFirstObjectByType<GrenadeAimVisualizer>();
			ref var grenadeAimComponent = ref _world.CreateSimpleEntity<GrenadeAimVisualizerComponent>();
			grenadeAimComponent.Value = grenadeAimVisualizer;
			if (grenadeAimVisualizer != null)
				grenadeAimVisualizer.Hide();


			// --- LookAtCursor singleton ---
			ref var lookAtCursor = ref _world.CreateSimpleEntity<LookAtCursor>();
			lookAtCursor.Transform = _player.transform;
			lookAtCursor.Mode3D = true;

			// --- Path drawing ---
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

		// Resolves the selected level and instantiates its prefab BEFORE SetupSpawnData,
		// so the Find* scans (SpawnPoint / MapLoot / AdditionalSpawn tag) pick up its content.
		// Prewarms inactive clones of every mob type that can appear on the level
		// (by MobId of all SpawnPoints) into the pool. This way the first waves take mobs via Pop()
		// instead of Instantiate and don't hitch.
		// The per-type count comes from MainHolder.MobPrewarmPerType (0 = disabled),
		// and doesn't exceed ActiveMobLimit — more mobs aren't alive at once anyway.
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
					// Warm each type only once, even if it appears at several points.
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
						MobSpawnSystem.SetupNewInstance(mob, mobConfig); // id + weapon override + helmet
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
					Debug.LogError($"[EntryPoint] Level prefab '{level.LevelPrefab.name}' has no LevelRoot.");
			}
			else
			{
				// Direct launch of the gameplay scene without choosing a level: content is already in the scene.
				_levelRoot = FindFirstObjectByType<LevelRoot>(FindObjectsInactive.Include);
			}

			_activeLevelConfig = _levelRoot != null && _levelRoot.LevelConfig != null
				? _levelRoot.LevelConfig
				: _levelConfig;

			if (_activeLevelConfig == null)
				Debug.LogError("[EntryPoint] Failed to resolve LevelConfig (no LevelRoot and no fallback config).");
		}

		// Scripted-level objects (gates / keys / zones / stage loot, see Docs/KeyLevelFeature.md). Keys start hidden
		// (inactive) — hence FindObjectsInactive.Include. No objects → no MissionComponent (MissionSystem no-ops).
		private void RegisterMissionObjects()
		{
			var gates = FindObjectsByType<LevelGate>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			var keys = FindObjectsByType<LevelKeyPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			var zones = FindObjectsByType<LevelZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			var stageLoots = FindObjectsByType<StageLoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			if (gates.Length + keys.Length + zones.Length + stageLoots.Length == 0)
				return;

			ref var mission = ref _world.CreateSimpleEntity<MissionComponent>();
			mission.Gates = new List<LevelGate>(gates);
			mission.Keys = new List<LevelKeyPickup>(keys);
			mission.Zones = new List<LevelZone>(zones);
			mission.StageLoots = new List<StageLoot>(stageLoots);
			mission.CollectedKeys = new HashSet<string>();
			mission.OpenedGates = new HashSet<string>();
		}

		// LevelRoot.PlayerStart: move the player there (facing = its Y rotation); the camera keeps its offset.
		private void PlacePlayerAtLevelStart()
		{
			var start = _levelRoot != null ? _levelRoot.PlayerStart : null;
			if (start == null)
				return;
			Vector3 delta = start.position - _player.transform.position;
			_player.transform.SetPositionAndRotation(start.position, Quaternion.Euler(0f, start.eulerAngles.y, 0f));
			_mainCamera.transform.position += delta;
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
				.Add(new LevelEventSystem())
				.Add(new MissionSystem())
				// Mob systems
				.Add(new MobSpawnSystem())
				.Add(new GroupSpawnSystem())
				.Add(new BreakableSpawnSystem())
				// Move and navigation systems
				.Add(new MobPathfindingSystem())
				.Add(new FormationSystem())
				.Add(new GrenadierSystem())
				.Add(new MeleeAttackerSystem())
				.Add(new RangedAttackerSystem())
				.Add(new MoveSystem())
				.Add(new MobSkidSystem()) // after MoveSystem (overrides its facing), before AnimationSystem/CrowdRenderSystem
				.Add(new CameraShakeSystem(apply: false)) // remove last frame's shake before following
				.Add(new FollowSystem())
				.Add(new LookAtCameraSystem())
				.Add(new LookAtCursorSystem())

				.Add(new AnimationSystem())
				.Add(new CrowdRenderSystem())
				.Add(new ModifierZoneSystem()) // oil slicks / ice hand out their modifiers before they tick
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
				.Add(new DamageFeedbackSystem())
				.Add(new DyingMobSystem())
				.Add(new BreakableSystem())
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
				.Add(new PlayerAnimationSystem())
				.Add(new CheckEndSystem())
				.Add(new FailSequenceSystem())
				.Add(new ScoreboardSystem()) // level scoreboards: deaths:frags (after the fail sequence starts)
				.Add(new CheerleaderSystem()) // NPC cheerleaders: dance / forgotten→zombies / victory pose / escape (before UILog, PauseSystem)
				.Add(new SmartConditionSystem())
				.Add(new AimVisualizerSystem())
				.Add(new UISystem())
				.Add(new UILogSystem())
				.Add(new PauseMenuSystem())
				.Add(new PauseSystem())
				.Add(new WorldFreezeSystem())
				.Add(new CameraShakeSystem(apply: true)) // last: the frame renders shaken
				.Init();
			#endregion
		}

		#endregion
	}
}
