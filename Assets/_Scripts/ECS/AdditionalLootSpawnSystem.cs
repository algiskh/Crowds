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

		// --- Синк "активные точки": если лут, на который указывала точка, исчез, возвращаем её в пул.
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

		// --- Каждый observer обрабатывается независимо. Один observer = максимум один
		// незавершённый запрос за раз, поэтому событие спауна сопоставляется строго
		// по SourceEntity == observerEntity (без этого observer'ы перехватывали бы
		// чужие события — отсюда «несколько лута» / зависший запрос).
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
				// Ждём результат именно нашего запроса. LootEntity < 0 => дроп-таблица
				// разыграла «пусто», лут не появился — освобождаем зарезервированную точку.
				if (!TryConsumeOwnSpawnEvent(world, lootSpawnedEventsPool, observerEntity, out int lootEntity))
					continue; // результат ещё не готов

				var point = observer.ProcessingPoint;
				observer.ProcessingPoint = null;
				observer.Process = SpawnProcess.Idle;

				if (point != null)
				{
					if (lootEntity >= 0)
						holder.ActivePoints[point] = world.PackEntity(lootEntity); // держим точку занятой, пока лут не подберут
					else
						ReturnPointToPool(ref holder, point); // пустой розыгрыш — точка снова свободна
				}

				TryStartCooldown(ref observer, ref holder);
			}
			else if (observer.Condition != null
				&& observer.Condition.IsFulfilled
				&& TryGetFreeLootPoint(holder.LootPointsPool, out var point))
			{
				// Резервируем точку немедленно, чтобы другой observer не занял ту же самую.
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
	/// Находит и поглощает событие спауна, принадлежащее именно этому observer'у
	/// (Source == AdditionalSpawn и SourceEntity == observerEntity). Возвращает id
	/// заспауненного лута (или -1, если лут не появился), удаляя событие.
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
