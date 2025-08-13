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
			var lookerPool = world.GetPool<LookerAtCamera>();

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
					effectRequest.EffectId = loot.LootType == LootType.Health ? "collectHealth" : "collect";
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
				var randomValue = UnityEngine.Random.value * Mathf.Clamp(cumulativeChance, 1f, float.MaxValue);

				if (randomValue > cumulativeChance)
				{
					world.DelEntity(entity);
					continue;
				}

				MobConfig.PossibleLoot selectedLoot = null;

				for (int i = possibleLoots.Length - 1; i >= 0; i--)
				{
					if (i > 0)
					{
						cumulativeChance -= possibleLoots[i].Chance;
						selectedLoot = possibleLoots[i];
						if (randomValue > cumulativeChance)
						{
							break;
						}
					}
				}
				//var selectedLoot = possibleLoots.FirstOrDefault(b => randomValue <= b.Chance);

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

					if (loot.SpriteLooker != null)
					{
						ref var lookerComponent = ref lookerPool.Add(lootEntity);
						lookerComponent.Transform = loot.SpriteLooker.transform;
						lookerComponent.FlatBillboard = true;

						var sprite = lootComponent.LootType != LootType.Weapon ?
							mainHolder.Value.SpriteHolder.GetSpriteById(selectedLoot.LootType.ToString()) :
							mainHolder.Value.GunConfigHolder.GetConfig(selectedLoot.Id).Preview;

						loot.SetSprite(sprite);
					}

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