using Leopotam.EcsLite;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class MobSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var spawnRequestPool = world.GetPool<MobSpawnRequestComponent>();

			var filter = world.Filter<MobSpawnRequestComponent>()
				.End();
			foreach ( var spawnEntity in filter)
			{
				ref var spawnRequest = ref spawnRequestPool.Get(spawnEntity);
				ref var spawnPoints = ref world.GetAsSingleton<SpawnPointsComponent>();
				ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
				ref var playerComponent = ref world.GetAsSingleton<PlayerComponent>();

				var mobConfig = spawnRequest.Config;
				var spawnPoint = spawnRequest.SpawnPoint;

				Mob mob = SpawnMob(mobPool, mobConfig);

				mob.transform.position = spawnPoint.position;
				var mobEntity = world.NewEntity();

				var mobComponentPool = world.GetPool<MobComponent>();
				var moveComponentPool = world.GetPool<MoveComponent>();
				var modifierComponentPool = world.GetPool<ModifierOwnerComponent>();
				var healthComponentPool = world.GetPool<HealthComponent>();
				var colliderComponentPool = world.GetPool<ColliderComponent>();
				var pathRecalculationPool = world.GetPool<PathRecalculation>();
				var lookerPool = world.GetPool<LookerAtCamera>();

				ref var mobComponent = ref mobComponentPool.Add(mobEntity);
				ref var moveComponent = ref moveComponentPool.Add(mobEntity);
				ref var modifierComponent = ref modifierComponentPool.Add(mobEntity);
				ref var healthComponent = ref healthComponentPool.Add(mobEntity);
				ref var colliderComponent = ref colliderComponentPool.Add(mobEntity);
				ref var pathRecalculationComponent = ref pathRecalculationPool.Add(mobEntity);
				ref var looker = ref lookerPool.Add(mobEntity);


				mobComponent.Value = mob;
				mobComponent.Config = mobConfig;
				mobComponent.Cooldown = 0;


				var playerPosition = playerComponent.Value.transform.position;
				moveComponent.Direction = new Vector2(playerPosition.x - spawnPoint.position.x, 0).normalized;
				moveComponent.Speed = mobConfig.Speed;
				moveComponent.Transform = mob.transform;
				healthComponent.CurrentHealth = mobConfig.Health;
				healthComponent.MaxHealth = mobConfig.Health;
				colliderComponent.CollisionType = CollisionType.Mob;
				colliderComponent.Value = mob.Collider;
				looker.Transform = mob.ValueBar.Transform;
				looker.FlatBillboard = true;

				InitializeMobGameObject(mob, mobConfig, playerPosition);
				world.DelEntity(spawnEntity);
			}
		}

		/// <summary>
		/// Spawn new mob or take used mob from pool
		/// </summary>
		private Mob SpawnMob(MobPoolComponent mobPool, MobConfig mobConfig)
		{
			Mob mob;
			if (mobPool.Value.Count > 0 &&
				mobPool.Value.Any(b => b.Id.Equals(mobConfig.Id)))
			{
				mob = mobPool.Value.First(mob => mob.Id.Equals(mobConfig.Id));
				mobPool.Value.Remove(mob);
			}
			else
			{
				mob = Object.Instantiate(
					mobConfig.Prefab,
					mobPool.Parent);
				mob.SetId(mobConfig.Id);
			}
			return mob;
		}

		/// <summary>
		/// Initialize mob game object with its configuration
		/// </summary>
		private void InitializeMobGameObject(Mob mob, MobConfig mobConfig, Vector2 playerPosition)
		{
			mob.ValueBar.SetMaxValue(mobConfig.Health)
						.ApplyValue(mobConfig.Health)
						.SetVisible(true);

			mob.gameObject.SetActive(true);
			//mob.SimpleAnimator.SetAnimation("Run");
		}
	}
}