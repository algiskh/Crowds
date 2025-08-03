using Leopotam.EcsLite;
using Unity.VisualScripting;
using UnityEngine;

namespace ECS
{
	public class SpawnPointSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			IterateSpawnPoints(world);
		}

		private void IterateSpawnPoints(EcsWorld world)
		{
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			var spawnPointPool = world.GetPool<SpawnPoint>();
			var player = world.GetAsSingleton<PlayerComponent>().Value;
			var playerPos = player.transform.position;
			var manager = world.GetAsSingleton<NavMeshManagerComponent>().Value;
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			ref var interSpawnCoolDown = ref world.GetAsSingleton<InterSpawnCooldown>();

			var spawnRequestPool = world.GetPool<SpawnRequest>();

			var filter = world.Filter<SpawnPoint>().End();
			var mobCount = world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();

			// todo: refactor
			ref var failWindow = ref world.GetAsSingleton<FailWindowComponent>();

			if (failWindow.Value.gameObject.activeSelf)
			{
				return;
			}

			if (interSpawnCoolDown.Value > 0)
			{
				interSpawnCoolDown.Value -= Time.deltaTime;
			}

			foreach (var entity in filter)
			{
				ref var spawnPoint = ref spawnPointPool.Get(entity);


				if (spawnPoint.Cooldown > 0)
				{
					spawnPoint.Cooldown -= Time.deltaTime;
					continue;
				}

				if (mobCount >= mainHolder.ActiveMobLimit || 
					playerPos.DistanceTo(spawnPoint.Value.transform.position) > manager.DistanceBetweenSectors
					|| interSpawnCoolDown.Value > 0)
				{
					continue; // Skip spawning if max mob count is reached
				}

				var mobEntity = world.NewEntity();
				ref var spawnRequest = ref spawnRequestPool.Add(mobEntity);
				spawnRequest.Prefab = mainHolder.Prefab;
				spawnRequest.SpawnPoint = spawnPoint.Value;
				spawnPoint.Cooldown = difficulty.SpawnCooldown;

				interSpawnCoolDown.Value = difficulty.SpawnCooldown / 2;
			}
		}
	}
}