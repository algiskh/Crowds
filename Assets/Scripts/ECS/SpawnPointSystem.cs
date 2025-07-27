using Leopotam.EcsLite;
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

			var spawnTimerPool = world.GetPool<SpawnTimer>();

			var spawnRequestPool = world.GetPool<SpawnRequest>();

			var filter = world.Filter<SpawnPoint>().Inc<SpawnTimer>().End();

			foreach (var entity in filter)
			{
				ref var spawnTimer = ref spawnTimerPool.Get(entity);
				if (spawnTimer.LastSpawnTime + mainHolder.SpawnCooldown > Time.time)
				{
					continue;
				}

				ref var spawnPoint = ref spawnPointPool.Get(entity);

				spawnTimer.LastSpawnTime = Time.time;

				var mobEntity = world.NewEntity();
				ref var mobComponent = ref spawnRequestPool.Add(mobEntity);
				mobComponent.Prefab = mainHolder.Prefab;
				mobComponent.SpawnPoint = spawnPoint.Value;
			}
		}
	}
}