using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class CheckSectorSystem : IEcsRunSystem
	{
		// Loop guard in case the player "jumped" several sectors in one frame.
		private const int MaxShiftsPerFrame = 8;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			ref var navmeshManager = ref world.GetAsSingleton<NavMeshManagerComponent>();
			var manager = navmeshManager.Value;

			var levelConfig = world.GetAsSingleton<CurrentLevelConfigComponent>().Value;
			if (levelConfig != null && levelConfig.SectorMode == SectorMode.Sliding)
			{
				RunSliding(player.Value, manager, levelConfig.ActiveSectorRadius);
				return;
			}

			RunRecycling(world, ref player, manager);
		}

		// Finite level: sectors are pre-placed and simply toggled on/off based on the player.
		private void RunSliding(Player player, NavMeshManager manager, int activeRadius)
		{
			var center = manager.UpdateActiveSectors(player.transform.position, activeRadius);
			if (center != null && player.CurrentSector != center)
				player.SetSector(center);
		}

		// Infinite scroll: 3 sectors are reused, objects on the "rear" sector are carried forward.
		private void RunRecycling(EcsWorld world, ref PlayerComponent player, NavMeshManager manager)
		{
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			var offset = mainHolder.Value.SectorUpdateOffset;
			var distanceBetweenSectorsHalf = manager.DistanceBetweenSectors / 2;

			if (player.Value.CurrentSector == null)
			{
				player.Value.SetSector(manager.CurrentSector);
				return;
			}

			float playerZ = player.Value.transform.position.z;

			// Hysteresis check. Loop to catch up with a fast player carry-over in one frame
			// (speed buff / low FPS), otherwise the player may leave the active navmesh bounds.
			for (int i = 0; i < MaxShiftsPerFrame; i++)
			{
				float currentSectorZ = manager.CurrentSector.transform.position.z;

				if (playerZ > currentSectorZ + distanceBetweenSectorsHalf + offset)
				{
					// Move right
					player.Value.SetSector(manager.RightSector);
					MoveStaticObjects(true, world, manager);
					manager.UpdateSectorsPosition(true);
				}
				else if (playerZ < currentSectorZ - distanceBetweenSectorsHalf - offset)
				{
					// Move left
					player.Value.SetSector(manager.LeftSector);
					MoveStaticObjects(false, world, manager);
					manager.UpdateSectorsPosition(false);
				}
				else
				{
					break;
				}
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

				if (health.CurrentHealth <= 0)
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
