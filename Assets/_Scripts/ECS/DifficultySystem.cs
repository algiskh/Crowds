using Leopotam.EcsLite;
using Sirenix.OdinInspector.Editor.GettingStarted;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LightTransport;

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

			if (difficulty.DifficultyTimer < 0 && (!difficulty.Stage.HasEndConditions || // check if timer is over
				(difficulty.Conditions.All(c => c == null || c.IsFulfilled)))) // check if all conditions are fulfilled
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

				if (difficulty.Conditions.Contains(smartCondition.Value))
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