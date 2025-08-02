using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class CheckSectorSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			ref var navmeshManager = ref world.GetAsSingleton<NavMeshManagerComponent>();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var currentSector = navmeshManager.Value.CurrentSector;
			var offset = mainHolder.Value.SectorUpdateOffset;
			var backwardOffset = -mainHolder.Value.SectorUpdateOffset;
			var distanceBetweenSectorsHalf = navmeshManager.Value.DistanceBetweenSectors / 2;
			if (player.Value.CurrentSector == null)
			{
				player.Value.SetSector(currentSector);
				return;
			}

			float playerZ = player.Value.transform.position.z;
			float currentSectorZ = currentSector.transform.position.z;

			// Проверка с гистерезисом
			if (playerZ > currentSectorZ + distanceBetweenSectorsHalf +  offset)
			{
				// Движение вправо
				player.Value.SetSector(navmeshManager.Value.RightSector);
				MoveStaticObjects(true, world, navmeshManager.Value);
				navmeshManager.Value.UpdateSectorsPosition(true);
			}
			else if (playerZ < currentSectorZ - distanceBetweenSectorsHalf - offset)
			{
				// Движение влево
				player.Value.SetSector(navmeshManager.Value.LeftSector);
				MoveStaticObjects(false, world, navmeshManager.Value);
				navmeshManager.Value.UpdateSectorsPosition(false);
			}
		}

		private void MoveStaticObjects(bool isMovingRight, EcsWorld world, NavMeshManager manager)
		{
			var decalPool = world.GetPool<DecalComponent>();
			var lootPool = world.GetPool<LootComponent>();
			var disposablePool = world.GetPool<DisposableComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var healthPool = world.GetPool<HealthComponent>();

			var sectorToMove = isMovingRight ? manager.LeftSector : manager.RightSector;

			var decalFilter = world.Filter<DecalComponent>().Inc<DisposableComponent>().End();
			foreach (var decalEntity in decalFilter)
			{
				ref var decal = ref decalPool.Get(decalEntity);
				ref var disposable = ref disposablePool.Get(decalEntity);
				if (disposable.IsDisposed)
					continue;

				if (decal.Value.transform.position.IsWithinXZBoundsFromMeshes(sectorToMove))
				{
					decal.Value.transform.position += 3 * 
						(isMovingRight ? manager.DistanceBetweenSectors * Vector3.forward : manager.DistanceBetweenSectors * Vector3.back);
				}
			}

			var lootFilter = world.Filter<LootComponent>().Inc<DisposableComponent>().End();

			foreach (var lootEntity in lootFilter)
			{
				ref var loot = ref lootPool.Get(lootEntity);
				ref var disposable = ref disposablePool.Get(lootEntity);
				if (disposable.IsDisposed)
					continue;
				if (loot.Loot.transform.position.IsWithinXZBoundsFromMeshes(sectorToMove))
				{
					loot.Loot.transform.position += 3 *
						(isMovingRight ? manager.DistanceBetweenSectors * Vector3.forward : manager.DistanceBetweenSectors * Vector3.back);
				}
			}

			var mobFilter = world.Filter<MobComponent>().Inc<HealthComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var mob = ref mobPool.Get(mobEntity);
				ref var health = ref healthPool.Get(mobEntity);

				if (health.CurrentHealth == 0)
					continue;

				if (mob.Value.transform.position.IsWithinXZBoundsFromMeshes(sectorToMove))
				{
					mob.Value.transform.position += 3 *
						(isMovingRight ? manager.DistanceBetweenSectors * Vector3.forward : manager.DistanceBetweenSectors * Vector3.back);
				}
			}
		}
	}
}