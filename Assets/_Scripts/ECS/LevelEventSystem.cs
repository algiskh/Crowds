using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Driver of scene LevelEventTriggers. Watches for the difficulty stage start (via
	/// DifficultyComponent.Stage.DifficultyLevel), arms the matching entries — instantiating and
	/// registering their optional smart-conditions EXACTLY at that moment, so accumulator conditions
	/// (FragsCondition) count from the stage boundary — and spawns breakables once the conditions
	/// are met (or immediately, if there are none).
	///
	/// Registered early (right after AdditionalLootSpawnSystem), so RequestSpawnBreakable
	/// is processed the same frame in BreakableSpawnSystem. IsFulfilled is read with the same
	/// one-frame delay as in DifficultySystem (SmartConditionSystem ticks at the end of the frame).
	/// See Docs/LevelEventsFeature.md.
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

			// 1) Stage boundary: arm the entries of the current level, disarm "stale" ones from others.
			if (stageChanged)
			{
				_hasLastLevel = true;
				_lastLevel = currentLevel;

				foreach (var entity in filter)
				{
					ref var observer = ref observerPool.Get(entity);

					if (observer.Level == currentLevel)
					{
						// A one-shot entry that already fired — don't re-arm it.
						if (observer.Fired && observer.Entry.Once)
							continue;
						ArmObserver(world, ref observer);
					}
					else if (observer.Armed)
					{
						// Left its own stage without firing — kill the live conditions.
						DisarmObserver(world, ref observer);
						observer.Armed = false;
					}
				}
			}

			// 2) Fire the armed entries whose conditions are met (or absent).
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

		// Arming: clean re-arm + copies of the conditions instantiated and registered as SmartConditionComponent.
		private static void ArmObserver(EcsWorld world, ref LevelEventObserverComponent observer)
		{
			DisarmObserver(world, ref observer); // in case of leftovers from the previous visit

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

		// Disarming: dispose the condition copies and delete their SmartConditionComponent entities
		// (same reference-matching trick as in DifficultySystem.FinishStage).
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
