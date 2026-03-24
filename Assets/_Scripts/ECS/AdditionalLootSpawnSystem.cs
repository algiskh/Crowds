using ECS;
using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Linq;
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
		ref var holder = ref world.GetAsSingleton<AdditionalLootSpawnHolderComponent>();
		ref var observer = ref world.GetAsSingleton<AdditionalLootObserverComponent>();

		if (observer.Cooldown > 0f)
		{
			observer.Cooldown -= Time.deltaTime;
			return;
		}

		var eventFilter = world.Filter<LootSpawnedEventComponent>().End(); // обрабатываем ивенты о спавне лута
		foreach (var entity in eventFilter)
		{
			var lootSpawnedEvent = lootSpawnedEventsPool.Get(entity);
			if (lootSpawnedEvent.Source == RequestSpawnSource.AdditionalSpawn
				&& !observer.ProcessingRequests.ContainsKey(entity))
			{
				observer.ProcessingRequests.Add(entity, lootSpawnedEvent.LootEntity);
				Debug.Log($"{nameof(AdditionalLootSpawnSystem)}: Added processing request for loot entity {lootSpawnedEvent.LootEntity}");
			}
		}

		//clear points that are not occupied by loot
		foreach (var kvp in holder.ActivePoints)
		{
			if (!lootPool.Has(kvp.Value))
			{
				holder.LootPointsPool.Add(kvp.Key);
			}
		}
		foreach (var point in holder.LootPointsPool)
		{
			if (holder.ActivePoints.ContainsKey(point))
				holder.ActivePoints.Remove(point);
		}

		if (observer.Process == SpawnProcess.Requesting)
		{
			if (holder.LootPointsPool.Contains(observer.ProcessingPoint) && observer.ProcessingRequests.Count > 0)
			{
				var kvp = observer.ProcessingRequests.First();
				world.DelEntity(kvp.Key); // delete event entity;
				holder.LootPointsPool.Remove(observer.ProcessingPoint);
				holder.ActivePoints.Add(observer.ProcessingPoint, kvp.Value);
				observer.ProcessingPoint = null;
				TryStartCooldown(ref observer, ref holder);
				observer.ProcessingRequests.Remove(kvp.Key);

				observer.Process = observer.ProcessingRequests.Count > 0 ? SpawnProcess.Requesting : SpawnProcess.Idle;
			}
		}
		else if (observer.Condition.IsFulfilled 
			&& TryGetFreeLootPoint(holder.LootPointsPool, lootPool, out var point))
			{
			ref var request = ref world.CreateSimpleEntity<RequestLootSpawn>();
			request.Position = point.position;
			request.PossibleLoots = observer.PossibleLoot;
			request.Source = RequestSpawnSource.AdditionalSpawn;
			observer.ProcessingPoint = point;
			observer.Process = SpawnProcess.Requesting;
		}

		Debug.Log($"{nameof(AdditionalLootSpawnSystem)}: Process = {observer.Process}, Condition = {observer.Condition.IsFulfilled}");
		Debug.Log($"{nameof(AdditionalLootSpawnSystem)}: Free points count = {holder.ActivePoints.Count(kvp => kvp.Value == -1)}");
	}

	private bool TryGetFreeLootPoint(IEnumerable<Transform> lootPoints, EcsPool<LootComponent> lootPool, out Transform point)
	{
		//can be extended with additional logic (i.e. looking for optimal distance)
		point = null;
		foreach (var lootPoint in lootPoints)
		{
			if (lootPoint != null) // point is free
			{
				point = lootPoint;
				return true;
			}
		}
		return false;
	}

	private void TryStartCooldown(ref AdditionalLootObserverComponent observer, ref AdditionalLootSpawnHolderComponent holder)
	{
		if (holder.CooldownMax > 0)
		{
			observer.Cooldown = holder.CooldownMax;
		}
	}
}
