using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class EntryPoint : MonoBehaviour
	{
		[SerializeField] private MainHolder _mainHolder;
		[SerializeField] private Transform _mobParent;

		[SerializeField] private Transform[] _spawnPoints;

		[SerializeField] private Player _player;
		[SerializeField] private Camera _mainCamera;

		[SerializeField] private Transform _bulletParent;

		private EcsWorld _world;
		private EcsSystems _systems;

		private void Awake()
		{
			_world = new EcsWorld();
			_systems = new EcsSystems(_world);

			SetupSpawnData();

			// Register your systems here
			#region RegisterSystems
			_systems.Add(new SpawnPointSystem())
				.Add(new MobSpawnSystem())
				.Add(new MobPathfindingSystem())
				.Add(new MoveSystem())
				.Add(new FollowSystem())
				.Add(new LookAtCameraSystem())
				.Add(new LookAtCursorSystem())

				.Add(new BulletOverlapSystem())

				.Add(new InputSystem())
				.Add(new EffectsSystem())
				.Add(new PlayerSystem())
				.Add(new BulletSystem())
				.Add(new PlayerMovementSystem())

			.Init();
			#endregion
		}

		private void SetupSpawnData()
		{
			var appEntity = _world.NewEntity();
			var configPool = _world.GetPool<MainHolderComponent>();
			var parentPool = _world.GetPool<MobParent>();
			var movementPool = _world.GetPool<MoveComponent>();
			var followerPool = _world.GetPool<FollowerComponent>();

			ref var config = ref configPool.Add(appEntity);
			ref var parent = ref parentPool.Add(appEntity);


			config.Value = _mainHolder;
			parent.Value = _mobParent;

			var spawnPointPool = _world.GetPool<SpawnPoint>();
			var spawnTimerPool = _world.GetPool<SpawnTimer>();

			

			foreach (var spawnPoint in _spawnPoints)
			{
				var spawnEntity = _world.NewEntity();
				ref var spawnPointComponent = ref spawnPointPool.Add(spawnEntity);
				ref var spawnTimer = ref spawnTimerPool.Add(spawnEntity);
				spawnTimer.LastSpawnTime = 0;
				spawnPointComponent.Value = spawnPoint;
			}

			ref var soundHolderComponent = ref _world.CreateSimpleEntity<SoundHolderComponent>();
			soundHolderComponent.Value = _mainHolder.SoundHolder;

			ref var spawnPointsComponent = ref _world.CreateSimpleEntity<SpawnPointsComponent>();

			spawnPointsComponent.Value = _spawnPoints;

			ref var spawnRequest = ref _world.CreateSimpleEntity<SpawnRequestComponent>();
			spawnRequest.MaxCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.MinCoolDown = _mainHolder.MinSpawnCoolDown;
			spawnRequest.CurrentCoolDown = _mainHolder.MaxSpawnCoolDown;
			spawnRequest.LastSpawnTime = 0;
			spawnRequest.IsBlocked = false;

			ref var effectsHolder = ref _world.CreateSimpleEntity<EffectsHolderComponent>();
			effectsHolder.Value = _mainHolder.EffectsHolder;

			ref var effectPool = ref _world.CreateSimpleEntity<EffectPoolComponent>();
			effectPool.Value = new();

			ref var bulletParent = ref _world.CreateSimpleEntity<BulletParentComponent>();
			bulletParent.Value = _bulletParent;

			var playerEntity = _world.NewEntity();
			var playerPool = _world.GetPool<PlayerComponent>();
			ref var playerComponent = ref playerPool.Add(playerEntity);
			playerComponent.Value = _player;
			ref var playerMovement = ref movementPool.Add(playerEntity);
			playerMovement.Speed = _mainHolder.PlayerConfig.Speed;
			var playerInputPool = _world.GetPool<PlayerInputComponent>();
			ref var playerInput = ref playerInputPool.Add(playerEntity);

			ref var mobPoolComponent = ref _world.CreateSimpleEntity<MobPoolComponent>();
			mobPoolComponent.Value = new();

			ref var bulletPool = ref _world.CreateSimpleEntity<BulletPoolComponent>();
			bulletPool.Value = new();


			var cameraFollowerEntity = _world.NewEntity();
			var followTargetPool = _world.GetPool<FollowTarget>();
			var followerOffset = _world.GetPool<FollowerOffset>();
			var cameraPool = _world.GetPool<CameraComponent>();  

			ref var follower = ref followerPool.Add(cameraFollowerEntity);
			follower.Value = _mainCamera.transform;

			ref var movement = ref movementPool.Add(cameraFollowerEntity);
			movement.Speed = _mainHolder.CameraSpeed;

			ref var followTarget = ref followTargetPool.Add(cameraFollowerEntity);
			followTarget = _mainHolder.CameraFollowTarget;
			followTarget.Target = _player.transform;

			ref var offset = ref followerOffset.Add(cameraFollowerEntity);
			offset.Value = _mainCamera.transform.position - _player.transform.position;

			ref var muzzle = ref _world.CreateSimpleEntity<MuzzleComponent>();
			muzzle.Weapon = _player.Weapon;
			muzzle.GunConfig = _mainHolder.GunConfig;
			muzzle.Count = _mainHolder.GunConfig.MagazineCapacity;


			var cameraEntity = _world.NewEntity();
			ref var cameraComponent = ref cameraPool.Add(cameraEntity);
			cameraComponent.Value = _mainCamera;

			ref var lookAtCursor = ref _world.CreateSimpleEntity<LookAtCursor>();

			lookAtCursor.Transform = _player.transform;
			lookAtCursor.Mode3D = true;


			var drawer = FindFirstObjectByType<PathGizmoDrawer>();
			drawer.Initialize(_world);
		}

		private void Update()
		{
			_systems?.Run();
		}

		private void OnDestroy()
		{
			if (_systems != null)
			{
				_systems.Destroy();
				_systems = null;
			}
			if (_world != null)
			{
				_world.Destroy();
				_world = null;
			}
		}
	}
}