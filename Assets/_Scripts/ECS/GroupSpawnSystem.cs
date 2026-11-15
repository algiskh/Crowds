using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	/// <summary>
	/// Spawns squads of mobs in formation at GroupSpawnPoints. On the point's cooldown it creates
	/// the leader (slot 0) and the followers (slots 1..N) at once, computes their local offsets via FormationTable
	/// and links the followers to the leader via FormationFollowerComponent (an EcsPackedEntity reference).
	/// Followers lose PathRecalculation/MovePath: they don't pathfind, they steer straight to the slot
	/// (FormationSystem → MoveComponent.Direction → MoveSystem).
	/// </summary>
	public sealed class GroupSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (world.GetAsSingleton<PauseStateComponent>().IsPaused)
				return;
			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				return;

			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			var mobConfigHolder = mainHolder.MobConfigHolder;
			if (mobConfigHolder == null)
				return;

			// Same dependencies as a regular point (SpawnPointSystem): the difficulty stage
			// sets the level/cooldown, the navmesh manager — the active spawn radius around the player.
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			var stage = difficulty.Stage;
			// Stage breather (DifficultyStage.RestTime) / finished wave timer: squads wait too.
			if (stage != null && !stage.IsSpawning(difficulty.DifficultyTimer))
				return;
			var level = stage.DifficultyLevel;
			var manager = world.GetAsSingleton<NavMeshManagerComponent>().Value;
			Vector3 playerPos = playerSingleton.Value.transform.position;
			int mobCount = world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();
			world.GetAsSingleton<CurrentLevelConfigComponent>().Value
				.GetSpawnDistance(manager.DistanceBetweenSectors, out float minDistance, out float maxDistance);

			float dt = Time.deltaTime;
			var pointPool = world.GetPool<GroupSpawnPointComponent>();
			var followerPool = world.GetPool<FormationFollowerComponent>();
			var recalcPool = world.GetPool<PathRecalculation>();
			var pathPool = world.GetPool<MovePath>();

			foreach (var entity in world.Filter<GroupSpawnPointComponent>().End())
			{
				ref var point = ref pointPool.Get(entity);
				if (point.Value == null || point.Value.Config == null)
					continue;

				if (point.Timer > 0f)
				{
					point.Timer -= dt;
					continue;
				}

				var config = point.Value.Config;
				Vector3 pointPos = point.Value.transform.position;

				// Same gating as a regular point: the mob limit and the distance ring around the player.
				if (mobCount >= mainHolder.ActiveMobLimit
					|| playerPos.DistanceTo(pointPos) > maxDistance
					|| playerPos.DistanceTo(pointPos) < minDistance)
					continue;

				// Level binding: no preset for the current stage — the squad doesn't spawn.
				if (!config.TryGetCooldown(level, out var cooldown))
					continue;

				SpawnGroup(world, point.Value.transform, config, mobConfigHolder,
					followerPool, recalcPool, pathPool);

				// The same cooldown acceleration over the stage as regular spawning.
				// DifficultyTimer counts DOWN: t = 1 at the stage start → base cooldown, t = 0 at the end → fastest.
				point.Timer = Mathf.Lerp(cooldown / stage.SpeedMultiplier, cooldown,
					difficulty.DifficultyTimer / stage.DifficultyTimer);
			}
		}

		private static void SpawnGroup(EcsWorld world, Transform origin, GroupSpawnConfig config,
			MobConfigHolder holder,
			EcsPool<FormationFollowerComponent> followerPool,
			EcsPool<PathRecalculation> recalcPool, EcsPool<MovePath> pathPool)
		{
			// Collect the member configs in slot order: slot 0 — the leader, then the followers.
			var members = new List<MobConfig>();
			var leaderConfig = holder.GetConfigById(config.LeaderMobId);
			if (leaderConfig == null)
				return;
			members.Add(leaderConfig);

			if (config.Followers != null)
			{
				foreach (var follower in config.Followers)
				{
					if (follower == null || string.IsNullOrEmpty(follower.MobId))
						continue;
					var followerConfig = holder.GetConfigById(follower.MobId);
					if (followerConfig == null)
						continue;
					for (int i = 0; i < follower.Count; i++)
						members.Add(followerConfig);
				}
			}

			var offsets = FormationTable.ComputeOffsets(
				config.Formation, members.Count, config.SpacingX, config.SpacingZ);

			Vector3 basePos = origin.position;
			Quaternion baseRot = origin.rotation;

			// --- Leader (slot 0) ---
			Vector3 leaderPos = basePos;
			if (NavMesh.SamplePosition(leaderPos, out var leaderHit, 3f, NavMesh.AllAreas))
				leaderPos = leaderHit.position;
			int leaderEntity = MobSpawnSystem.CreateMob(world, members[0], leaderPos);
			var packedLeader = world.PackEntity(leaderEntity);

			// --- Followers (slots 1..N) ---
			int count = Mathf.Min(members.Count, offsets.Length);
			for (int j = 1; j < count; j++)
			{
				Vector3 slotWorld = basePos + baseRot * offsets[j];
				if (NavMesh.SamplePosition(slotWorld, out var hit, 3f, NavMesh.AllAreas))
					slotWorld = hit.position;

				int followerEntity = MobSpawnSystem.CreateMob(world, members[j], slotWorld);

				// Followers don't pathfind: remove the recalc timer and the path so only the formation leads them.
				if (recalcPool.Has(followerEntity))
					recalcPool.Del(followerEntity);
				if (pathPool.Has(followerEntity))
					pathPool.Del(followerEntity);

				ref var follower = ref followerPool.Add(followerEntity);
				follower.Leader = packedLeader;
				follower.SlotOffset = offsets[j];
				follower.InFormation = false;
			}
		}
	}
}
