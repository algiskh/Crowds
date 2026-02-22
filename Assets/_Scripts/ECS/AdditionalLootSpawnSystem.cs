using ECS;
using Leopotam.EcsLite;
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

		ref var additionalLootSpawn = ref world.GetAsSingleton<AdditionalLootSpawnComponent>();
		var filter = world.Filter<AdditionalLootObserverComponent>().End();

		foreach (var entity in filter)
		{
			ref var observer = ref conditionsPool.Get(entity);

			if (!observer.IsFulfilled && observer.Condition.IsFulfilled)
			{
				// ищем свободную точку
				// если нашли, выполняем спаун
				// если нет, то break
				foreach (var loot in observer.PossibleLoot)
				{
					// вычисляем возможный лут
				}
				// помечаем свободный лут
			}
		}
	}
}
