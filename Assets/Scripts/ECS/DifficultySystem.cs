using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class DifficultySystem : IEcsInitSystem, IEcsRunSystem
	{
		private float _startTime;

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			ref var difficulty = ref world.CreateSimpleEntity<DifficultyComponent>();
			difficulty.SpawnCooldown = mainHolder.MaxSpawnCoolDown; // Изначально устанавливаем максимальное значение
			difficulty.DifficultyTimer = mainHolder.DifficultyIncreaseTime; // Устанавливаем время увеличения сложности

			ref var interSpawnCoolDown = ref world.CreateSimpleEntity<InterSpawnCooldown>();
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			ref var interSpawnCoolDown = ref world.GetAsSingleton<InterSpawnCooldown>();

			// Считаем прогресс от 0 до 1
			float timePassed = Mathf.Clamp01(difficulty.DifficultyTimer / mainHolder.DifficultyIncreaseTime);

			// Линейная интерполяция от Max к Min
			difficulty.SpawnCooldown = Mathf.Lerp(
				mainHolder.MinSpawnCoolDown,   // from
				mainHolder.MaxSpawnCoolDown,   // to
				timePassed                     // progress
			);

			difficulty.DifficultyTimer -= Time.deltaTime;
			UnityEngine.Debug.Log($"!!! SpawnCooldown: {difficulty.SpawnCooldown}");
			// Пример для SpeedMultiplier, если надо делать его, например, от 1 до mainHolder.MaxSpeedMultiplier:
			// difficulty.SpeedMultiplier = Mathf.Lerp(1f, mainHolder.MaxSpeedMultiplier, timePassed);

			// difficulty.DifficultyAccelerationTime (можно обновлять или использовать по необходимости)
		}
	}
}