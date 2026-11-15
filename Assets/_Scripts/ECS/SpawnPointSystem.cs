using Leopotam.EcsLite;
using Unity.VisualScripting;
using UnityEngine;

namespace ECS
{
	public class SpawnPointSystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var interspanwCooldown = ref world.CreateSimpleEntity<InterSpawnCooldownComponent>();
			interspanwCooldown.Value = 0f; 
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

			IterateSpawnPoints(world);
		}

		private void IterateSpawnPoints(EcsWorld world)
		{
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			var spawnPointPool = world.GetPool<SpawnPointComponent>();
			var player = world.GetAsSingleton<PlayerComponent>().Value;
			var playerPos = player.transform.position;
			var manager = world.GetAsSingleton<NavMeshManagerComponent>().Value;
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			ref var currentLevel = ref world.GetAsSingleton<CurrentLevelConfigComponent>();
			ref var interspawnCooldown = ref world.GetAsSingleton<InterSpawnCooldownComponent>();

			var spawnRequestPool = world.GetPool<MobSpawnRequestComponent>();

			var filter = world.Filter<SpawnPointComponent>().End();
			var mobCount = world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();

			// Stage breather (DifficultyStage.RestTime) or a finished wave timer (SpawnOnlyWhileTimer): no spawns.
			if (difficulty.Stage != null && !difficulty.Stage.IsSpawning(difficulty.DifficultyTimer))
				return;
			// Wave quota (DifficultyStage.MobQuota): only ids with quota left, stop when it's spent.
			bool hasQuota = WaveQuota.Sync(world, difficulty.Stage);
			System.Collections.Generic.Dictionary<string, int> quotaLeft = null;
			if (hasQuota)
			{
				ref var quota = ref world.GetAsSingleton<WaveQuotaComponent>();
				if (quota.RemainingTotal <= 0)
					return;
				quotaLeft = quota.Remaining;
			}
			currentLevel.Value.GetSpawnDistance(manager.DistanceBetweenSectors, out float minDistance, out float maxDistance);

			if (interspawnCooldown.Value > 0)
			{
				interspawnCooldown.Value -= Time.deltaTime;
				return; // Skip spawning if cooldown is active
			}

			foreach (var entity in filter)
			{
				ref var spawnPoint = ref spawnPointPool.Get(entity);


				if (spawnPoint.Timer > 0 || interspawnCooldown.Value > 0)
				{
					spawnPoint.Timer -= Time.deltaTime;
					continue;
				}

				if (mobCount >= mainHolder.ActiveMobLimit
					|| playerPos.DistanceTo(spawnPoint.Value.transform.position) > maxDistance
					|| playerPos.DistanceTo(spawnPoint.Value.transform.position) < minDistance)
				{
					continue; // Skip spawning
				}

				var stage = difficulty.Stage;
				var level = difficulty.Stage.DifficultyLevel;

				MobSpawnConfig spawnConfig;
				bool picked = hasQuota
					? spawnPoint.Value.TryGetRandomSpawnConfig(level,
						c => quotaLeft.TryGetValue(c.MobId, out int left) && left > 0, out spawnConfig)
					: spawnPoint.Value.TryGetRandomSpawnConfig(level, out spawnConfig);
				if (picked)
				{

					if (spawnConfig == default)
					{
						Debug.LogAssertion($"Spawn point {spawnPoint.Value.name} has no spawn config for level {level}!");
						continue;
					}

					var overallCooldown = spawnConfig.GetCooldown(level);
					var mobConfig = mainHolder.MobConfigHolder.GetConfigById(spawnConfig.MobId);

					var spawnRequestEntity = world.NewEntity();
					ref var spawnRequest = ref spawnRequestPool.Add(spawnRequestEntity);
					spawnRequest.Config = mobConfig;
					spawnRequest.SpawnPoint = spawnPoint.Value.transform;


					// DifficultyTimer counts DOWN: t = 1 at the stage start (base cooldown) → 0 at its end
					// (cooldown / SpeedMultiplier), so spawning accelerates over the stage.
					var newTimer = Mathf.Lerp(overallCooldown / stage.SpeedMultiplier, overallCooldown, difficulty.DifficultyTimer / stage.DifficultyTimer);

					spawnPoint.Timer = newTimer;
					interspawnCooldown.Value = stage.InterSpawnCooldown;

					if (hasQuota)
					{
						ref var quota = ref world.GetAsSingleton<WaveQuotaComponent>();
						WaveQuota.Spend(ref quota, spawnConfig.MobId);
						if (quota.RemainingTotal <= 0)
							break;
					}
				}
			}
		}
	}
}