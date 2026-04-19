using ECS;
using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

public class AdditionalLootSpawnSystem : IEcsInitSystem, IEcsRunSystem
{
	public void Init(IEcsSystems systems)
	{
		var world = systems.GetWorld();

		ref var additionalLootSpawn = ref world.GetAsSingleton<AdditionalLootSpawnHolderComponent>();

		foreach (var additionalLoot in additionalLootSpawn.LootConfigs)
		{
			foreach (var kvp in additionalLoot.AdditionalLoot)
			{
				var condition = kvp.Key.GetCopyUntyped();
				condition.Initialize(world);

				ref var observer = ref world.CreateSimpleEntity<AdditionalLootObserverComponent>();
				observer.Condition = condition;
				observer.PossibleLoot = kvp.Value;
				observer.Process = SpawnProcess.Idle;
				observer.ProcessingRequests = new();

				ref var smartCondition = ref world.CreateSimpleEntity<SmartConditionComponent>();
				smartCondition.Value = condition;
			}
		}
	}

	public void Run(IEcsSystems systems)
	{
		var world = systems.GetWorld();
		var lootSpawnedEventsPool = world.GetPool<LootSpawnedEventComponent>();
		var lootPool = world.GetPool<LootComponent>();
		var observerPool = world.GetPool<AdditionalLootObserverComponent>();
		ref var holder = ref world.GetAsSingleton<AdditionalLootSpawnHolderComponent>();

		// --- Синк "активные точки": если лут, на который указывала точка, исчез, возвращаем её в пул.
		if (holder.ActivePoints != null && holder.ActivePoints.Count > 0)
		{
			List<Transform> reclaimed = null;
			foreach (var kvp in holder.ActivePoints)
			{
				if (!lootPool.Has(kvp.Value))
					(reclaimed ??= new List<Transform>()).Add(kvp.Key);
			}
			if (reclaimed != null)
			{
				for (int i = 0; i < reclaimed.Count; i++)
				{
					var point = reclaimed[i];
					holder.ActivePoints.Remove(point);
					if (!holder.LootPointsPool.Contains(point))
						holder.LootPointsPool.Add(point);
				}
			}
		}

		// --- Каждый observer обрабатывается независимо.
		var observerFilter = world.Filter<AdditionalLootObserverComponent>().End();
		foreach (var observerEntity in observerFilter)
		{
			ref var observer = ref observerPool.Get(observerEntity);

			if (observer.Cooldown > 0f)
			{
				observer.Cooldown -= Time.deltaTime;
				continue;
			}

			// --- Принимаем события спауна: только свои (для своего observer'а).
			var eventFilter = world.Filter<LootSpawnedEventComponent>().End();
			foreach (var eventEntity in eventFilter)
			{
				var ev = lootSpawnedEventsPool.Get(eventEntity);
				if (ev.Source == RequestSpawnSource.AdditionalSpawn
					&& !observer.ProcessingRequests.ContainsKey(eventEntity))
				{
					observer.ProcessingRequests.Add(eventEntity, ev.LootEntity);
				}
			}

			if (observer.Process == SpawnProcess.Requesting)
			{
				if (holder.LootPointsPool.Contains(observer.ProcessingPoint) && observer.ProcessingRequests.Count > 0)
				{
					// Забираем первый элемент словаря без LINQ.
					KeyValuePair<int, int> request = default;
					foreach (var kvp in observer.ProcessingRequests)
					{
						request = kvp;
						break;
					}

					world.DelEntity(request.Key); // delete event entity
					holder.LootPointsPool.Remove(observer.ProcessingPoint);
					holder.ActivePoints.Add(observer.ProcessingPoint, request.Value);
					observer.ProcessingPoint = null;
					TryStartCooldown(ref observer, ref holder);
					observer.ProcessingRequests.Remove(request.Key);

					observer.Process = observer.ProcessingRequests.Count > 0 ? SpawnProcess.Requesting : SpawnProcess.Idle;
				}
			}
			else if (observer.Condition != null
				&& observer.Condition.IsFulfilled
				&& TryGetFreeLootPoint(holder.LootPointsPool, out var point))
			{
				ref var request = ref world.CreateSimpleEntity<RequestLootSpawn>();
				request.Position = point.position;
				request.PossibleLoots = observer.PossibleLoot;
				request.Source = RequestSpawnSource.AdditionalSpawn;
				observer.ProcessingPoint = point;
				observer.Process = SpawnProcess.Requesting;
			}
		}
	}

	/// <summary>
	/// Берёт первую свободную точку из пула. LootPointsPool семантически хранит
	/// только свободные точки (занятые живут в ActivePoints), поэтому достаточно
	/// пропустить null-ссылки и вернуть первую валидную.
	/// </summary>
	private bool TryGetFreeLootPoint(List<Transform> freePool, out Transform point)
	{
		point = null;
		if (freePool == null) return false;
		for (int i = 0; i < freePool.Count; i++)
		{
			var candidate = freePool[i];
			if (candidate != null)
			{
				point = candidate;
				return true;
			}
		}
		return false;
	}

	private void TryStartCooldown(ref AdditionalLootObserverComponent observer, ref AdditionalLootSpawnHolderComponent holder)
	{
		if (holder.CooldownMax > 0)
			observer.Cooldown = holder.CooldownMax;
	}
}