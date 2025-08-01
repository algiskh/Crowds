using Leopotam.EcsLite;
using Sirenix.OdinInspector;
using UnityEngine;

namespace ECS
{
	public class EntryPoint : MonoBehaviour
	{
		#region FIELDS

		[Title("Основные ссылки")]
		[SerializeField, Required, BoxGroup("Game References")] private MainHolder _mainHolder;
		[SerializeField, Required, BoxGroup("Game References")] private Player _player;
		[SerializeField, Required, BoxGroup("Game References")] private Camera _mainCamera;

		[Space]
		[Title("Родители объектов")]
		[SerializeField, Required, BoxGroup("Parents")] private Transform _mobParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _bulletParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _effectParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _decalParent;
		[SerializeField, Required, BoxGroup("Parents")] private Transform _lootParent;
		[Space]
		[Title("Точки спауна мобов")]
		[SerializeField, Required, ListDrawerSettings, BoxGroup("Spawn Points")]
		private Transform[] _spawnPoints;

		// ECS
		private EcsWorld _world;
		private EcsSystems _systems;

		#endregion

		#region UNITY EVENTS

		private void Awake()
		{
			_world = new EcsWorld();
			_systems = new EcsSystems(_world);

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
			// --- Общие сущности приложения ---
			int appEntity = _world.NewEntity();
			ref var config = ref _world.GetPool<MainHolderComponent>().Add(appEntity);
			config.Value = _mainHolder;

			ref var navMeshManager = ref _world.CreateSimpleEntity<NavMeshManagerComponent>();
			navMeshManager.Value = FindFirstObjectByType<NavMeshManager>();

			// --- Точки спауна ---
			var spawnPointPool = _world.GetPool<SpawnPoint>();
			var spawnTimerPool = _world.GetPool<SpawnTimer>();
			foreach (var spawnPoint in _spawnPoints)
			{
				int spawnEntity = _world.NewEntity();
				ref var sp = ref spawnPointPool.Add(spawnEntity);
				ref var st = ref spawnTimerPool.Add(spawnEntity);
				sp.Value = spawnPoint;
				st.LastSpawnTime = 0;
			}
			ref var spawnPointsComponent = ref _world.CreateSimpleEntity<SpawnPointsComponent>();
			spawnPointsComponent.Value = _spawnPoints;

			// --- Система спауна ---
			ref var spawnRequest = ref _world.CreateSimpleEntity<SpawnRequestComponent>();
			spawnRequest.MaxCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.MinCoolDown = _mainHolder.MinSpawnCoolDown;
			spawnRequest.CurrentCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.LastSpawnTime = 0;
			spawnRequest.IsBlocked = false;

			// --- Хранилища и пулы ---
			ref var soundHolderComponent = ref _world.CreateSimpleEntity<SoundHolderComponent>();
			soundHolderComponent.Value = _mainHolder.SoundHolder;

			ref var effectsHolder = ref _world.CreateSimpleEntity<EffectsHolderComponent>();
			effectsHolder.Value = _mainHolder.EffectsHolder;

			ref var decalsHolder = ref _world.CreateSimpleEntity<DecalsHolderComponent>();
			decalsHolder.Value = _mainHolder.DecalsConfigHolder;

			ref var effectPool = ref _world.CreateSimpleEntity<EffectPoolComponent>();
			effectPool.Value = new();
			effectPool.Parent = _effectParent;

			ref var mobPoolComponent = ref _world.CreateSimpleEntity<MobPoolComponent>();
			mobPoolComponent.Value = new();

			ref var bulletPool = ref _world.CreateSimpleEntity<BulletPoolComponent>();
			bulletPool.Value = new();
			bulletPool.Parent = _bulletParent;

			ref var decalPool = ref _world.CreateSimpleEntity<DecalPoolComponent>();
			decalPool.Value = new();
			decalPool.Parent = _decalParent;

			ref var lootPool = ref _world.CreateSimpleEntity<LootPoolComponent>();
			lootPool.Value = new();
			lootPool.Parent = _lootParent;

			// --- Игрок ---
			int playerEntity = _world.NewEntity();
			ref var playerComponent = ref _world.GetPool<PlayerComponent>().Add(playerEntity);
			playerComponent.Value = _player;
			_player.Initialize(playerEntity);
			ref var playerMovement = ref _world.GetPool<MoveComponent>().Add(playerEntity);
			playerMovement.Speed = _mainHolder.PlayerConfig.Speed;
			ref var playerInput = ref _world.GetPool<PlayerInputComponent>().Add(playerEntity);

			// --- Камера ---
			int cameraFollowerEntity = _world.NewEntity();
			ref var follower = ref _world.GetPool<FollowerComponent>().Add(cameraFollowerEntity);
			follower.Value = _mainCamera.transform;
			ref var movement = ref _world.GetPool<MoveComponent>().Add(cameraFollowerEntity);
			movement.Speed = _mainHolder.CameraSpeed;
			ref var followTarget = ref _world.GetPool<FollowTarget>().Add(cameraFollowerEntity);
			followTarget = _mainHolder.CameraFollowTarget;
			followTarget.Target = _player.transform;
			ref var offset = ref _world.GetPool<FollowerOffset>().Add(cameraFollowerEntity);
			offset.Value = _mainCamera.transform.position - _player.transform.position;

			int cameraEntity = _world.NewEntity();
			ref var cameraComponent = ref _world.GetPool<CameraComponent>().Add(cameraEntity);
			cameraComponent.Value = _mainCamera;

			// --- Оружие/Магазин ---
			ref var muzzle = ref _world.CreateSimpleEntity<MuzzleComponent>();
			muzzle.Weapon = _player.Weapon;
			muzzle.GunConfig = _mainHolder.GunConfig;
			muzzle.Count = _mainHolder.GunConfig.MagazineCapacity;

			// --- Система LookAtCursor ---
			ref var lookAtCursor = ref _world.CreateSimpleEntity<LookAtCursor>();
			lookAtCursor.Transform = _player.transform;
			lookAtCursor.Mode3D = true;

			// --- Гизмо для отрисовки пути ---
			var drawer = FindFirstObjectByType<PathGizmoDrawer>();
			if (drawer != null)
				drawer.Initialize(_world);
		}

		private void RegisterSystems()
		{
			// --- Регистрируем ECS системы ---
			#region RegisterSystems
			_systems
				.Add(new CheckSectorSystem())
				.Add(new SpawnPointSystem())
				.Add(new MobSpawnSystem())
				.Add(new MobPathfindingSystem())
				.Add(new MoveSystem())
				.Add(new FollowSystem())
				.Add(new LookAtCameraSystem())
				.Add(new LookAtCursorSystem())
				.Add(new BulletSystem())
				.Add(new BulletOverlapSystem())
				.Add(new CollisionSystem())
				.Add(new DamageSystem())
				.Add(new LootSystem())
				.Add(new InputSystem())
				.Add(new EffectsSystem())
				.Add(new DecalSystem())
				.Add(new PlayerSystem())
				.Add(new PlayerMovementSystem())
				.Init();
			#endregion
		}

		#endregion
	}
}
