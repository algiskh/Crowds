using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Scripted-level glue between the difficulty stages and the scene mission objects (MissionComponent):
	///  • on each stage start — opens LevelGates with OpenMode.OnStage for that stage, shows LevelKeyPickups whose
	///    AppearOnStage matches, spawns StageLoot, and puts DifficultyStage.Objective into the ObjectiveBanner;
	///  • every frame — key pickup by proximity (→ CollectedKeys), key gates open when the player brings the key,
	///    gate/key animations; in wave mode (SpawnOnlyWhileTimer) after the spawn window the banner adds
	///    "Осталось: N".
	/// Stage flow itself stays in DifficultySystem; stages wait on GateOpenedCondition / PlayerInZoneCondition /
	/// MobsClearedCondition. Registered right after LevelEventSystem. No-op on levels without mission objects.
	/// See Docs/KeyLevelFeature.md.
	/// </summary>
	public sealed class MissionSystem : IEcsRunSystem
	{
		private ObjectiveBanner _banner;
		private DifficultyStage _stage;
		private int _shownRemaining = -1;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			if (!world.TryGetAsSingleton<MissionComponent>(out _))
				return;
			ref var mission = ref world.GetAsSingleton<MissionComponent>();
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();

			_banner ??= new ObjectiveBanner();
			_banner.EnsureCreated();
			_banner.Tick(Time.unscaledDeltaTime);

			if (world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive)
			{
				_banner.Hide();
				return;
			}

			var stage = difficulty.Stage;
			if (stage != null && stage != _stage)
			{
				_stage = stage;
				_shownRemaining = -1;
				OnStageStart(world, ref mission, stage);
			}

			bool paused = world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused;
			if (paused)
				return;
			float dt = Time.deltaTime;

			if (!world.TryGetAsSingleton<PlayerComponent>(out var player) || player.Value == null)
				return;
			Vector3 playerPos = player.Value.transform.position;

			// --- Keys: animate + pick up ---
			if (mission.Keys != null)
			{
				foreach (var key in mission.Keys)
				{
					if (key == null || !key.IsAvailable)
						continue;
					key.Tick(dt);
					if (FlatDistance(playerPos, key.transform.position) > key.PickupRadius)
						continue;

					key.Collect();
					mission.CollectedKeys.Add(key.KeyId);
					Log(world, key.PickupMessage);
					Effect(world, "collect", key.transform.position);
				}
			}

			// --- Gates: key opening + animation ---
			if (mission.Gates != null)
			{
				foreach (var gate in mission.Gates)
				{
					if (gate == null)
						continue;
					if (!gate.IsOpen && gate.Mode == LevelGate.OpenMode.WithKey &&
					    mission.CollectedKeys.Contains(gate.RequiredKey) &&
					    FlatDistance(playerPos, gate.transform.position) <= gate.KeyOpenRadius)
					{
						OpenGate(world, ref mission, gate);
					}
					// "The way back is closed": shut an open gate once the player is deep enough in the next area.
					else if (gate.IsOpen && !string.IsNullOrEmpty(gate.CloseWhenPlayerInZone) &&
					         FlatDistance(playerPos, gate.transform.position) >= gate.CloseMinDistance &&
					         IsInZone(mission, gate.CloseWhenPlayerInZone, playerPos))
					{
						gate.Close();
						Log(world, gate.CloseMessage);
					}
					gate.Tick(dt);
				}
			}

			UpdateRemainingCounter(world, ref difficulty);
		}

		private void OnStageStart(EcsWorld world, ref MissionComponent mission, DifficultyStage stage)
		{
			var level = stage.DifficultyLevel;

			if (mission.Gates != null)
				foreach (var gate in mission.Gates)
					if (gate != null && gate.Mode == LevelGate.OpenMode.OnStage && gate.OpenOnStage == level)
						OpenGate(world, ref mission, gate);

			if (mission.Keys != null)
				foreach (var key in mission.Keys)
					if (key != null && !key.IsAvailable && key.AppearOnStage == level &&
					    !mission.CollectedKeys.Contains(key.KeyId))
					{
						key.Appear();
						if (!string.IsNullOrEmpty(key.AppearEffectId))
							Effect(world, key.AppearEffectId, key.transform.position);
					}

			if (mission.StageLoots != null)
				foreach (var stageLoot in mission.StageLoots)
					if (stageLoot != null && stageLoot.OnStage == level)
						SpawnStageLoot(world, stageLoot);

			_banner.SetText(stage.Objective);
		}

		private static void OpenGate(EcsWorld world, ref MissionComponent mission, LevelGate gate)
		{
			if (gate.IsOpen)
				return;
			gate.Open();
			mission.OpenedGates.Add(gate.Id);
			Log(world, gate.OpenMessage);
		}

		private static void SpawnStageLoot(EcsWorld world, StageLoot stageLoot)
		{
			var loots = stageLoot.Loot;
			if (loots == null || loots.Length == 0)
				return;

			var center = stageLoot.transform.position;
			var pool = world.GetPool<RequestLootSpawn>();
			for (int i = 0; i < loots.Length; i++)
			{
				var entry = loots[i];
				if (entry == null)
					continue;
				float angle = i * Mathf.PI * 2f / loots.Length;
				Vector3 offset = loots.Length > 1
					? new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * stageLoot.Spread
					: Vector3.zero;

				ref var request = ref pool.Add(world.NewEntity());
				request.Position = center + offset;
				request.SourceEntity = -1;
				request.Source = RequestSpawnSource.MapLoot; // no despawn timer
				request.PossibleLoots = new[]
				{
					new PossibleLoot
					{
						LootType = entry.LootType,
						Id = entry.Id,
						Count = entry.Count,
						AmmoCaliber = entry.AmmoCaliber,
						Chance = 1f
					}
				};
				Effect(world, "collect", request.Position);
			}
		}

		// Wave mode: after the spawn window, show how many mobs are left (rebuilds the string only on change).
		private void UpdateRemainingCounter(EcsWorld world, ref DifficultyComponent difficulty)
		{
			var stage = difficulty.Stage;
			if (stage == null)
				return;
			// Quota waves: alive + still to spawn, from the first second. Timer waves: alive, once spawning stopped.
			bool quotaWave = stage.HasMobQuota;
			if (!quotaWave && (!stage.SpawnOnlyWhileTimer || difficulty.DifficultyTimer > 0f))
				return;

			int remaining = world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();
			if (quotaWave)
				remaining += WaveQuota.RemainingTotal(world, stage) +
				             world.Filter<MobSpawnRequestComponent>().End().GetEntitiesCount();
			if (remaining == _shownRemaining)
				return;
			_shownRemaining = remaining;
			string counter = "Осталось: " + remaining;
			_banner.SetText(string.IsNullOrEmpty(stage.Objective) ? counter : stage.Objective + "\n" + counter);
		}

		/// <summary>True when <paramref name="position"/> is inside any LevelZone with <paramref name="zoneId"/>.</summary>
		public static bool IsInZone(in MissionComponent mission, string zoneId, Vector3 position)
		{
			if (mission.Zones == null)
				return false;
			foreach (var zone in mission.Zones)
				if (zone != null && zone.Id == zoneId && zone.Contains(position))
					return true;
			return false;
		}

		private static float FlatDistance(Vector3 a, Vector3 b)
		{
			float dx = a.x - b.x, dz = a.z - b.z;
			return Mathf.Sqrt(dx * dx + dz * dz);
		}

		private static void Log(EcsWorld world, string message)
		{
			if (string.IsNullOrEmpty(message))
				return;
			ref var log = ref world.CreateSimpleEntity<RequestUILogComponent>();
			log.Message = message;
		}

		private static void Effect(EcsWorld world, string id, Vector3 position)
		{
			ref var fx = ref world.CreateSimpleEntity<RequestEffectComponent>();
			fx.EffectId = id;
			fx.Position = position;
		}
	}
}
