using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class DifficultySystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var levelConfig = world.GetAsSingleton<CurrentLevelConfigComponent>();
			ref var difficulty = ref world.CreateSimpleEntity<DifficultyComponent>();
			var firstStage = levelConfig.Value.GetFirstStage(true); // TODO: Extend
			ApplyStage(world, ref difficulty, firstStage);
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region Check pause
			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
			{
				return;
			}
			#endregion

			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			var levelConfig = world.GetAsSingleton<CurrentLevelConfigComponent>();

			difficulty.DifficultyTimer -= Time.deltaTime;

			bool allConditionsMet = true;
			var conditions = difficulty.Conditions;

			for (int i = 0; i < conditions.Length; i++)
			{
				var c = conditions[i];
				if (c != null && !c.IsFulfilled)
				{
					allConditionsMet = false;
					break;
				}
			}

			if (difficulty.DifficultyTimer < 0 && (!difficulty.Stage.HasEndConditions || // check if timer is over
				allConditionsMet)) // check if all conditions are fulfilled
			{
				FinishStage(world, ref difficulty, levelConfig);
			}
		}

		#region Handling stages
		private void ApplyStage(EcsWorld world, ref DifficultyComponent difficulty, DifficultyStage stage)
		{
			difficulty.Stage = stage;
			difficulty.DifficultyTimer = stage.DifficultyTimer;

			if (stage.ShowTimer)
			{
				ref var requestShowDifficulty = ref world.CreateSimpleEntity<RequestShowDifficultyComponent>();
				requestShowDifficulty.DifficultyLevel = difficulty.Stage.DifficultyLevel;
				requestShowDifficulty.Seconds = difficulty.DifficultyTimer;
			}
			else
			{
				ref var requestHideDifficulty = ref world.CreateSimpleEntity<RequestHideDifficultyComponent>();
			}

			if (stage.EndConditions == null || stage.EndConditions.Length == 0)
				return;

			difficulty.Conditions = new ISmartCondition[stage.EndConditions.Length];

			for (int i = 0; i < stage.EndConditions.Length; i++)
			{
				var condition = stage.EndConditions[i];

				if (condition != null)
				{
					ref var conditionEntity = ref world.CreateSimpleEntity<SmartConditionComponent>();
					var newCondition = condition.GetCopyUntyped();
					newCondition.Initialize(world);
					conditionEntity.Value = newCondition;
					difficulty.Conditions[i] = newCondition;
				}
			}
		}

		private void FinishStage(EcsWorld world, ref DifficultyComponent difficulty, CurrentLevelConfigComponent currentLevel)
		{
			var level = difficulty.Stage.DifficultyLevel;
			var conditionsPool = world.GetPool<SmartConditionComponent>();
			foreach (var condition in difficulty.Conditions)
			{
				condition?.Dispose();
			}
			var filter = world.Filter<SmartConditionComponent>().End();
			foreach (var entity in filter)
			{
				var smartCondition = conditionsPool.Get(entity);

				bool contains = false;
				var conditions = difficulty.Conditions;
				var value = smartCondition.Value;

				for (int i = 0; i < conditions.Length; i++)
				{
					if (conditions[i] == value)
					{
						contains = true;
						break;
					}
				}

				if (contains)
				{
					conditionsPool.Del(entity);
				}
			}

			var newStage = currentLevel.Value.GetNextStage(level);

			if (newStage == null)
			{
				// No more stages, reset to first stage
				newStage ??= currentLevel.Value.GetFirstStage(true);
			}
			else
			if (newStage.DifficultyLevel == DifficultyLevel.finish)
			{
				// Reached the end of the difficulty stages, trigger win condition
				ref var endGameComponent = ref world.CreateSimpleEntity<EndGameComponent>();
				endGameComponent.isWin = true;
				// Optionally, you can also trigger any end-game logic here
			}
			ApplyStage(world, ref difficulty, newStage);
		}
		#endregion
	}
}