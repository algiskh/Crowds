using ECS;
using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;


public class AdditionalLootSpawnSystem : IEcsInitSystem, IEcsRunSystem
{
	public void Init(IEcsSystems systems)
	{
		var world = systems.GetWorld();

		ref var additionalLootSpawn = ref world.GetAsSingleton<AdditionalLootSpawnComponent>();

		foreach (var additionalLoot in additionalLootSpawn.LootConfigs)
		{
			foreach (var kvp in additionalLoot.AdditionalLoot)
			{
				var condition = kvp.Key.GetCopyUntyped();
				condition.Initialize(world);

				ref var lootConfigEcs = ref world.CreateSimpleEntity<AdditionalLootObserverComponent>();
				lootConfigEcs.Condition = condition;
				lootConfigEcs.PossibleLoot = kvp.Value;

				ref var smartCondition = ref world.CreateSimpleEntity<SmartConditionComponent>();
				smartCondition.Value = condition;
			}
		}
	}

	public void Run(IEcsSystems systems)
	{
		var world = systems.GetWorld();
		var conditionsPool = world.GetPool<AdditionalLootObserverComponent>();
		var lootSpawnedEventsPool = world.GetPool<LootSpawnedEventComponent>();
		var lootPool = world.GetPool<LootComponent>();
		ref var additionalLootSpawn = ref world.GetAsSingleton<AdditionalLootSpawnComponent>();

		Dictionary<int, int> entityToObserver = new Dictionary<int, int>();

		var lootSpawnedFilter = world.Filter<LootSpawnedEventComponent>().End();
		foreach (var entity in lootSpawnedFilter)
		{
			ref var lootSpawnedEvent = ref lootSpawnedEventsPool.Get(entity);

			entityToObserver.Add(entity, lootSpawnedEvent.SourceEntity);
		}


		var filter = world.Filter<AdditionalLootObserverComponent>().End();
		foreach (var entity in filter)
		{
			ref var observer = ref conditionsPool.Get(entity);

			//clear points that are not occupied by loot
			foreach (var kvp in additionalLootSpawn.LootPoints)
			{
				if (kvp.Value != -1 && !lootPool.Has(kvp.Value))
				{
					additionalLootSpawn.LootPoints[kvp.Key] = -1; // point is free
				}
			}

				if (observer.Process == SpawnProcess.Requesting)
			{
				foreach (var kvp in entityToObserver)
				{
					if (kvp.Value == entity)
					{
						observer.Process = SpawnProcess.Spawning;

						if (additionalLootSpawn.LootPoints.ContainsKey(observer.ProcessingPoint))
						{
							additionalLootSpawn.LootPoints[observer.ProcessingPoint] = kvp.Key; // ToDO вставлять entity lootComponent
							observer.Process = SpawnProcess.Idle;
							world.DelEntity(kvp.Key);
						}
						break;
					}
				}

				continue; // уже запрашиваем спавн, ждем результата
			}

			if (observer.Process is SpawnProcess.Idle && observer.Condition.IsFulfilled)
			{
				if (TryGetFreeLootPoint(additionalLootSpawn.LootPoints, lootPool, out var point))
				{
					ref var request = ref world.CreateSimpleEntity<RequestLootSpawn>();
					request.Position = point.position;
					request.PossibleLoots = observer.PossibleLoot;
					request.SourceEntity = entity;
					observer.ProcessingPoint = point;
					observer.Process = SpawnProcess.Requesting;
				}
			}
		}
	}

	private bool TryGetFreeLootPoint(Dictionary<Transform, int> lootPoints, EcsPool<LootComponent> lootPool, out Transform point)
	{
		point = null;
		foreach (var kvp in lootPoints)
		{
			if (kvp.Value == -1) // point is free
			{
				point = kvp.Key;
				return true;
			}

			var loot = lootPool.Get(kvp.Value);

			if (loot.Loot == null || !loot.Loot.gameObject.activeSelf)
			{
				point = kvp.Key;
				return true;
			}
		}
		return false;
	}
}
