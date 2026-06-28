using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	/// <summary>
	/// Спаунит отряды мобов в строю по точкам GroupSpawnPoint. По кулдауну точки разом создаёт
	/// ведущего (слот 0) и ведомых (слоты 1..N), считает их локальные офсеты по FormationTable
	/// и связывает ведомых с ведущим через FormationFollowerComponent (ссылка — EcsPackedEntity).
	/// Ведомым снимается PathRecalculation/MovePath: они не патфайндят, а рулят к слоту напрямую
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

			// Те же зависимости, что и у обычной точки (SpawnPointSystem): стадия сложности
			// задаёт уровень/кулдаун, навменеджер — радиус активного спауна вокруг игрока.
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			var stage = difficulty.Stage;
			var level = stage.DifficultyLevel;
			var manager = world.GetAsSingleton<NavMeshManagerComponent>().Value;
			Vector3 playerPos = playerSingleton.Value.transform.position;
			int mobCount = world.Filter<MobComponent>().Inc<HealthComponent>().End().GetEntitiesCount();

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

				// Гейтинг как у обычной точки: лимит мобов и кольцо дистанции вокруг игрока.
				if (mobCount >= mainHolder.ActiveMobLimit
					|| playerPos.DistanceTo(pointPos) > manager.DistanceBetweenSectors
					|| playerPos.DistanceTo(pointPos) < manager.DistanceBetweenSectors / 4)
					continue;

				// Привязка к уровню: нет пресета для текущей стадии — отряд не спаунится.
				if (!config.TryGetCooldown(level, out var cooldown))
					continue;

				SpawnGroup(world, point.Value.transform, config, mobConfigHolder,
					followerPool, recalcPool, pathPool);

				// Тот же разгон кулдауна по ходу стадии, что и у обычного спауна.
				point.Timer = Mathf.Lerp(cooldown, cooldown / stage.SpeedMultiplier,
					difficulty.DifficultyTimer / stage.DifficultyTimer);
			}
		}

		private static void SpawnGroup(EcsWorld world, Transform origin, GroupSpawnConfig config,
			MobConfigHolder holder,
			EcsPool<FormationFollowerComponent> followerPool,
			EcsPool<PathRecalculation> recalcPool, EcsPool<MovePath> pathPool)
		{
			// Собираем конфиги участников в порядке слотов: слот 0 — ведущий, далее ведомые.
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

			// --- Ведущий (слот 0) ---
			Vector3 leaderPos = basePos;
			if (NavMesh.SamplePosition(leaderPos, out var leaderHit, 3f, NavMesh.AllAreas))
				leaderPos = leaderHit.position;
			int leaderEntity = MobSpawnSystem.CreateMob(world, members[0], leaderPos);
			var packedLeader = world.PackEntity(leaderEntity);

			// --- Ведомые (слоты 1..N) ---
			int count = Mathf.Min(members.Count, offsets.Length);
			for (int j = 1; j < count; j++)
			{
				Vector3 slotWorld = basePos + baseRot * offsets[j];
				if (NavMesh.SamplePosition(slotWorld, out var hit, 3f, NavMesh.AllAreas))
					slotWorld = hit.position;

				int followerEntity = MobSpawnSystem.CreateMob(world, members[j], slotWorld);

				// Ведомые не патфайндят: убираем таймер пересчёта и путь, чтобы их вёл только строй.
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
