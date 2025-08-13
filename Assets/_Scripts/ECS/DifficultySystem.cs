using Leopotam.EcsLite;
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

			if (difficulty.DifficultyTimer < 0)
			{
				var level = difficulty.Stage.DifficultyLevel;
				var newStage = levelConfig.Value.GetNextStage(level);
				if (newStage == null)
				{
					// No more stages, reset to first stage
					newStage = levelConfig.Value.GetFirstStage(true);
				}
				ApplyStage(world, ref difficulty, newStage);
				ref var requestShowDifficulty = ref world.CreateSimpleEntity<RequestShowDifficultyComponent>();
				requestShowDifficulty.DifficultyLevel = level;
				requestShowDifficulty.Seconds = difficulty.DifficultyTimer;
			}
		}

		private void ApplyStage(EcsWorld world, ref DifficultyComponent difficulty, DifficultyStage stage)
		{
			difficulty.Stage = stage;
			difficulty.DifficultyTimer = stage.DifficultyTimer;

			ref var requestShowDifficulty = ref world.CreateSimpleEntity<RequestShowDifficultyComponent>();
			requestShowDifficulty.DifficultyLevel = stage.DifficultyLevel;
			requestShowDifficulty.Seconds = difficulty.DifficultyTimer;
		}
	}
}