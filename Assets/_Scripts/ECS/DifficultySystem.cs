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
			ApplyStage(ref difficulty, firstStage);
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

			if (difficulty.DifficultyTimer < 0)
			{
				var level = difficulty.Stage.DifficultyLevel;
				var newStage = levelConfig.Value.GetNextStage(level);
				if (newStage == null)
				{
					// No more stages, reset to first stage
					newStage = levelConfig.Value.GetFirstStage(true);
				}
				ApplyStage(ref difficulty, newStage);
				ref var requestShowDifficulty = ref world.CreateSimpleEntity<RequestShowDifficultyComponent>();
				requestShowDifficulty.DifficultyLevel = level;
				requestShowDifficulty.Seconds = difficulty.DifficultyTimer;
			}
		}

		private void ApplyStage(ref DifficultyComponent difficulty, DifficultyStage stage)
		{
			difficulty.Stage = stage;
			difficulty.DifficultyTimer = stage.DifficultyTimer;
		}
	}
}