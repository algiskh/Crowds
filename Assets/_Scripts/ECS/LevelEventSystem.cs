using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Драйвер сценовых LevelEventTrigger'ов. Отслеживает старт стейджа сложности (по
	/// DifficultyComponent.Stage.DifficultyLevel), вооружает подходящие записи — инстансирует и
	/// регистрирует их опциональные smart-условия ИМЕННО в этот момент, чтобы условия-аккумуляторы
	/// (FragsCondition) считались от границы стейджа — и выполняет спаун breakable'ов, когда условия
	/// выполнены (или сразу, если условий нет).
	///
	/// Регистрируется рано (сразу после AdditionalLootSpawnSystem), чтобы RequestSpawnBreakable
	/// обработался тем же кадром в BreakableSpawnSystem. Чтение IsFulfilled идёт с той же
	/// однокадровой задержкой, что и в DifficultySystem (SmartConditionSystem тикает в конце кадра).
	/// См. Docs/LevelEventsFeature.md.
	/// </summary>
	public sealed class LevelEventSystem : IEcsInitSystem, IEcsRunSystem
	{
		private bool _hasLastLevel;
		private DifficultyLevel _lastLevel;

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (!world.TryGetAsSingleton<LevelEventHolderComponent>(out var holder) || holder.Triggers == null)
				return;

			foreach (var trigger in holder.Triggers)
			{
				if (trigger == null)
					continue;

				var entries = trigger.Entries;
				if (entries == null)
					continue;

				for (int i = 0; i < entries.Count; i++)
				{
					var entry = entries[i];
					if (entry == null)
						continue;

					ref var observer = ref world.CreateSimpleEntity<LevelEventObserverComponent>();
					observer.Entry = entry;
					observer.Origin = trigger.transform;
					observer.Level = entry.OnStageStart;
					observer.Conditions = null;
					observer.Armed = false;
					observer.Fired = false;
				}
			}
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			if (!world.TryGetAsSingleton<DifficultyComponent>(out var difficulty) || difficulty.Stage == null)
				return;

			var observerPool = world.GetPool<LevelEventObserverComponent>();
			var filter = world.Filter<LevelEventObserverComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;

			var currentLevel = difficulty.Stage.DifficultyLevel;
			bool stageChanged = !_hasLastLevel || currentLevel != _lastLevel;

			// 1) Граница стейджа: вооружаем записи текущего уровня, разоружаем «зависшие» с других.
			if (stageChanged)
			{
				_hasLastLevel = true;
				_lastLevel = currentLevel;

				foreach (var entity in filter)
				{
					ref var observer = ref observerPool.Get(entity);

					if (observer.Level == currentLevel)
					{
						// Одноразовая запись, которая уже отработала — не переармливаем.
						if (observer.Fired && observer.Entry.Once)
							continue;
						ArmObserver(world, ref observer);
					}
					else if (observer.Armed)
					{
						// Ушли со «своего» стейджа не выстрелив — гасим живые условия.
						DisarmObserver(world, ref observer);
						observer.Armed = false;
					}
				}
			}

			// 2) Выстрел вооружённых записей, чьи условия выполнены (или отсутствуют).
			foreach (var entity in filter)
			{
				ref var observer = ref observerPool.Get(entity);
				if (!observer.Armed)
					continue;

				if (!ConditionsMet(observer.Conditions))
					continue;

				Fire(world, ref observer);

				DisarmObserver(world, ref observer);
				observer.Armed = false;
				observer.Fired = true;
			}
		}

		// Вооружение: чистая переармировка + инстанс копий условий и их регистрация как SmartConditionComponent.
		private static void ArmObserver(EcsWorld world, ref LevelEventObserverComponent observer)
		{
			DisarmObserver(world, ref observer); // на случай остатков от прошлого визита

			var wrappers = observer.Entry.Conditions;
			if (wrappers != null && wrappers.Length > 0)
			{
				var conditions = new List<ISmartCondition>(wrappers.Length);
				for (int i = 0; i < wrappers.Length; i++)
				{
					var wrapper = wrappers[i];
					if (wrapper == null)
						continue;

					var condition = wrapper.GetCopyUntyped();
					if (condition == null)
						continue;

					condition.Initialize(world);

					ref var conditionEntity = ref world.CreateSimpleEntity<SmartConditionComponent>();
					conditionEntity.Value = condition;
					conditions.Add(condition);
				}

				observer.Conditions = conditions.Count > 0 ? conditions.ToArray() : null;
			}
			else
			{
				observer.Conditions = null;
			}

			observer.Armed = true;
		}

		// Разоружение: диспоузим копии условий и удаляем их SmartConditionComponent-сущности
		// (тот же приём сопоставления по ссылке, что в DifficultySystem.FinishStage).
		private static void DisarmObserver(EcsWorld world, ref LevelEventObserverComponent observer)
		{
			var conditions = observer.Conditions;
			if (conditions == null || conditions.Length == 0)
			{
				observer.Conditions = null;
				return;
			}

			foreach (var condition in conditions)
				condition?.Dispose();

			var pool = world.GetPool<SmartConditionComponent>();
			var filter = world.Filter<SmartConditionComponent>().End();
			foreach (var entity in filter)
			{
				var value = pool.Get(entity).Value;
				for (int i = 0; i < conditions.Length; i++)
				{
					if (conditions[i] == value)
					{
						pool.Del(entity);
						break;
					}
				}
			}

			observer.Conditions = null;
		}

		private static bool ConditionsMet(ISmartCondition[] conditions)
		{
			if (conditions == null || conditions.Length == 0)
				return true;

			for (int i = 0; i < conditions.Length; i++)
				if (conditions[i] != null && !conditions[i].IsFulfilled)
					return false;

			return true;
		}

		private static void Fire(EcsWorld world, ref LevelEventObserverComponent observer)
		{
			var spawns = observer.Entry.Spawns;
			if (spawns == null)
				return;

			for (int i = 0; i < spawns.Length; i++)
			{
				var spawn = spawns[i];
				if (spawn == null || string.IsNullOrEmpty(spawn.ConfigId))
					continue;

				var point = spawn.Point != null ? spawn.Point : observer.Origin;
				Vector3 pos = point != null ? point.position : Vector3.zero;
				world.RequestSpawnBreakable(spawn.ConfigId, pos, spawn.Rotation);
			}
		}
	}
}
