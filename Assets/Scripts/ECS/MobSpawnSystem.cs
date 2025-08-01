﻿using Leopotam.EcsLite;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class MobSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			// Spawn 1 mob at a time, if cooldown is over
			ref var spawnRequest = ref world.GetAsSingleton<SpawnRequestComponent>();

			var currentTime = Time.time;

			#region HandlingRequest
			if (spawnRequest.CurrentCoolDown + spawnRequest.LastSpawnTime <= currentTime && !spawnRequest.IsBlocked)
			{
				spawnRequest.LastSpawnTime = currentTime;

				spawnRequest.CurrentCoolDown = Random.Range(spawnRequest.MinCoolDown, spawnRequest.MaxCoolDown);

				ref var spawnPoints = ref world.GetAsSingleton<SpawnPointsComponent>();
				ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
				ref var mainConfig = ref world.GetAsSingleton<MainHolderComponent>();
				ref var playerComponent = ref world.GetAsSingleton<PlayerComponent>();

				var mobConfig = mainConfig.Value.MobConfig; // add more mobconfigs
				var spawnPoint = spawnPoints.Value.GetRandomElement();


				Mob mob = SpawnMob(mobPool, mobConfig);

				mob.transform.position = spawnPoint.position;
				var mobEntity = world.NewEntity();

				var mobComponentPool = world.GetPool<MobComponent>();
				var moveComponentPool = world.GetPool<MoveComponent>();
				var healthComponentPool = world.GetPool<HealthComponent>();
				var colliderComponentPool = world.GetPool<ColliderComponent>();
				var pathRecalculationPool = world.GetPool<PathRecalculation>();
				var lookerPool = world.GetPool<LookerAtCamera>();

				ref var mobComponent = ref mobComponentPool.Add(mobEntity);
				ref var moveComponent = ref moveComponentPool.Add(mobEntity);
				ref var healthComponent = ref healthComponentPool.Add(mobEntity);
				ref var colliderComponent = ref colliderComponentPool.Add(mobEntity);
				ref var pathRecalculationComponent = ref pathRecalculationPool.Add(mobEntity);
				ref var looker = ref lookerPool.Add(mobEntity);


				mobComponent.Value = mob;
				mobComponent.Config = mobConfig;

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
			}
			#endregion
		}

		/// <summary>
		/// Spawn new mob or take used mob from pool
		/// </summary>
		private Mob SpawnMob(MobPoolComponent mobPool, MobConfig mobConfig)
		{
			Mob mob;
			if (mobPool.Value != null &&
				mobPool.Value.Count > 0 &&
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