using Leopotam.EcsLite;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class LootSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var requestLootSpawnPool = world.GetPool<RequestLootSpawn>();
			ref var lootMainPool = ref world.GetAsSingleton<LootPoolComponent>();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			ref var navmeshManager = ref world.GetAsSingleton<NavMeshManagerComponent>();
			var lootPool = world.GetPool<LootComponent>();
			var collisionPool = world.GetPool<ColliderComponent>();
			var disposablePool = world.GetPool<DisposableComponent>();
			var currentSectorPool = world.GetPool<CurrentSectorComponent>();

			#region CheckingDisposed
			// Check disposed loots and return them to the pool
			var disposedFilter = world.Filter<LootComponent>().Inc<DisposableComponent>().End();
			foreach (var disposedEntity in disposedFilter)
			{
				ref var loot = ref lootPool.Get(disposedEntity);
				ref var disposable = ref disposablePool.Get(disposedEntity);
				if (disposable.IsDisposed)
				{
					loot.Loot.gameObject.SetActive(false);
					lootMainPool.Value.Push(loot.Loot);

					//Request Effect
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = "collect";
					effectRequest.Position = loot.Loot.transform.position;

					world.DelEntity(disposedEntity); // delete entity
				}
			}
			#endregion

			#region HandlingRequests
			var filter = world.Filter<RequestLootSpawn>().End();
			foreach (var entity in filter)
			{
				ref var requestLootSpawn = ref requestLootSpawnPool.Get(entity);

				var possibleLoots = requestLootSpawn.PossibleLoots;
				var cumulativeChance = possibleLoots.Sum(b => b.Chance);

				// Select loot based on chance  
				var randomValue = UnityEngine.Random.value;

				if (randomValue > cumulativeChance)
				{
					continue;
				}

				var selectedLoot = possibleLoots.FirstOrDefault(b => randomValue <= b.Chance);

				if (selectedLoot != null)
				{
					Loot loot;
					if (lootMainPool.Value != null &&
						lootMainPool.Value.Count > 0)
					{
						loot = lootMainPool.Value.Pop();
					}
					else
					{
						loot = Object.Instantiate( // Fixed ambiguous reference  
							mainHolder.Value.LootPrefab,
							lootMainPool.Parent);
					}

					var lootEntity = world.NewEntity();
					ref var lootComponent = ref lootPool.Add(lootEntity);
					ref var collisionComponent = ref collisionPool.Add(lootEntity);
					ref var disposableComponent = ref disposablePool.Add(lootEntity);

					// Ensure LootComponent has LootType and Value properties  
					lootComponent.LootType = selectedLoot.LootType;
					lootComponent.Count = selectedLoot.Count;
					lootComponent.Loot = loot;
					lootComponent.Id = selectedLoot.Id;
					disposableComponent.IsDisposed = false;
					loot.gameObject.SetActive(true);
					loot.transform.position = requestLootSpawn.Position;

					collisionComponent.CollisionType = CollisionType.Loot;
					//collisionComponent.Radius = mainHolder.Value.DefaultCollisionRadius;
				}
				world.DelEntity(entity); // delete request entity
			}
			#endregion
		}
	}
}