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

		// --- Sync "active points": if the loot a point referenced disappeared, return it to the pool.
		if (holder.ActivePoints != null && holder.ActivePoints.Count > 0)
		{
			List<Transform> reclaimed = null;
			foreach (var kvp in holder.ActivePoints)
			{
				// Unpack returns false once the loot entity is deleted/recycled — safe even
				// after the id is reused (gen mismatch). Then double-check the component.
				if (!kvp.Value.Unpack(world, out var lootEntity) || !lootPool.Has(lootEntity))
					(reclaimed ??= new List<Transform>()).Add(kvp.Key);
			}
			if (reclaimed != null)
			{
				for (int i = 0; i < reclaimed.Count; i++)
				{
					var point = reclaimed[i];
					holder.ActivePoints.Remove(point);
					ReturnPointToPool(ref holder, point);
				}
			}
		}

		// --- Each observer is processed independently. One observer = at most one
		// pending request at a time, so the spawn event is matched strictly
		// by SourceEntity == observerEntity (without this observers would intercept
		// each other's events — hence "multiple loot" / a hung request).
		var observerFilter = world.Filter<AdditionalLootObserverComponent>().End();
		foreach (var observerEntity in observerFilter)
		{
			ref var observer = ref observerPool.Get(observerEntity);

			if (observer.Cooldown > 0f)
			{
				observer.Cooldown -= Time.deltaTime;
				continue;
			}

			if (observer.Process == SpawnProcess.Requesting)
			{
				// Wait for the result of exactly our request. LootEntity < 0 => the drop table
				// rolled "empty", no loot appeared — free the reserved point.
				if (!TryConsumeOwnSpawnEvent(world, lootSpawnedEventsPool, observerEntity, out int lootEntity))
					continue; // result not ready yet

				var point = observer.ProcessingPoint;
				observer.ProcessingPoint = null;
				observer.Process = SpawnProcess.Idle;

				if (point != null)
				{
					if (lootEntity >= 0)
						holder.ActivePoints[point] = world.PackEntity(lootEntity); // keep the point occupied until the loot is picked up
					else
						ReturnPointToPool(ref holder, point); // empty roll — the point is free again
				}

				TryStartCooldown(ref observer, ref holder);
			}
			else if (observer.Condition != null
				&& observer.Condition.IsFulfilled
				&& TryGetFreeLootPoint(world, ref holder, out var point))
			{
				// Reserve the point immediately so another observer doesn't take the same one.
				holder.LootPointsPool.Remove(point);

				ref var request = ref world.CreateSimpleEntity<RequestLootSpawn>();
				request.Position = point.position;
				request.PossibleLoots = observer.PossibleLoot;
				request.Source = RequestSpawnSource.AdditionalSpawn;
				request.SourceEntity = observerEntity;

				observer.ProcessingPoint = point;
				observer.Process = SpawnProcess.Requesting;
			}
		}
	}

	/// <summary>
	/// Finds and consumes the spawn event belonging to exactly this observer
	/// (Source == AdditionalSpawn and SourceEntity == observerEntity). Returns the id
	/// of the spawned loot (or -1 if no loot appeared), deleting the event.
	/// </summary>
	private bool TryConsumeOwnSpawnEvent(EcsWorld world, EcsPool<LootSpawnedEventComponent> pool,
		int observerEntity, out int lootEntity)
	{
		lootEntity = -1;
		var filter = world.Filter<LootSpawnedEventComponent>().End();
		foreach (var eventEntity in filter)
		{
			ref var ev = ref pool.Get(eventEntity);
			if (ev.Source == RequestSpawnSource.AdditionalSpawn && ev.SourceEntity == observerEntity)
			{
				lootEntity = ev.LootEntity;
				world.DelEntity(eventEntity);
				return true;
			}
		}
		return false;
	}

	private void ReturnPointToPool(ref AdditionalLootSpawnHolderComponent holder, Transform point)
	{
		if (point != null && !holder.LootPointsPool.Contains(point))
			holder.LootPointsPool.Add(point);
	}

	/// <summary>
	/// Picks a free point. LootPointsPool semantically stores only free points (occupied ones live in ActivePoints).
	/// Classic levels: the first valid one. Scripted levels with LevelZones (MissionComponent): if the player stands
	/// in a zone that holds loot points, only free points of that zone are used ("drop on this platform"; none free →
	/// wait); outside such zones — the free point nearest to the player.
	/// </summary>
	private bool TryGetFreeLootPoint(EcsWorld world, ref AdditionalLootSpawnHolderComponent holder, out Transform point)
	{
		point = null;
		var freePool = holder.LootPointsPool;
		if (freePool == null) return false;

		if (world.TryGetAsSingleton<MissionComponent>(out var mission) && mission.Zones != null && mission.Zones.Count > 0
			&& world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null)
		{
			Vector3 playerPos = player.Value.transform.position;
			LevelZone playerZone = null;
			foreach (var zone in mission.Zones)
			{
				if (zone != null && zone.Contains(playerPos) && ZoneHasLootPoint(zone, freePool, holder.ActivePoints))
				{
					playerZone = zone;
					break;
				}
			}

			float best = float.MaxValue;
			for (int i = 0; i < freePool.Count; i++)
			{
				var candidate = freePool[i];
				if (candidate == null) continue;
				var pos = candidate.position;
				if (playerZone != null && !playerZone.Contains(pos)) continue;
				float distSqr = (pos - playerPos).sqrMagnitude;
				if (distSqr < best)
				{
					best = distSqr;
					point = candidate;
				}
			}
			return point != null;
		}

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

	private static bool ZoneHasLootPoint(LevelZone zone, List<Transform> freePool,
		Dictionary<Transform, EcsPackedEntity> activePoints)
	{
		for (int i = 0; i < freePool.Count; i++)
			if (freePool[i] != null && zone.Contains(freePool[i].position))
				return true;
		if (activePoints != null)
			foreach (var kvp in activePoints)
				if (kvp.Key != null && zone.Contains(kvp.Key.position))
					return true;
		return false;
	}

	private void TryStartCooldown(ref AdditionalLootObserverComponent observer, ref AdditionalLootSpawnHolderComponent holder)
	{
		if (holder.CooldownMax > 0)
			observer.Cooldown = holder.CooldownMax;
	}
}
