using Leopotam.EcsLite;
using System.Diagnostics;

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
				// Движение вперед
				player.Value.SetSector(navmeshManager.Value.RightSector);
				navmeshManager.Value.UpdateSectorsPosition(true);
				UnityEngine.Debug.Log($"ChangingSystem Forward. Now sector is {player.Value.CurrentSector.name}");
			}
			else if (playerZ < currentSectorZ - distanceBetweenSectorsHalf - offset)
			{
				// Движение назад
				player.Value.SetSector(navmeshManager.Value.LeftSector);
				navmeshManager.Value.UpdateSectorsPosition(false);
				UnityEngine.Debug.Log($"ChangingSystem Backward. Now sector is {player.Value.CurrentSector.name}");
			}
			// добавить метод для перемещения всех активных объектов над смещеннным сектором
		}
	}
}