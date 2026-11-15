using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Drives follower mobs in formation (§6/§7c of the instructions, minimal version without navmesh).
	/// Every frame: world slot = leader.position + leader.rotation * SlotOffset
	/// (the leader's rotation is already smoothed by MoveSystem, so we don't compute a separate formationDir).
	/// The follower steers to the slot via MoveComponent.Direction; MoveDirect moves by
	/// Speed * |Direction|, so we regulate speed by the Direction length (closer to the slot — slower,
	/// far — catching up). When the leader dies (Unpack == false) the follower returns to the regular
	/// chase after the player (Promote). Must run after MobPathfindingSystem and before MoveSystem.
	/// </summary>
	public sealed class FormationSystem : IEcsRunSystem
	{
		private const float FormationTime = 0.5f; // §7c: target speed = dist / FormationTime
		private const float Precision = 1.0f;     // §7c base slot-reach tolerance (m)
		private const float MinSpeedFrac = 0.15f; // don't crawl slower than this fraction of base speed
		private const float MaxSpeedFrac = 1.6f;  // allow catching up faster than base speed

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused)
				return;

			var followerPool = world.GetPool<FormationFollowerComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var mobPool = world.GetPool<MobComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<FormationFollowerComponent>()
				.Inc<MoveComponent>()
				.Inc<MobComponent>()
				.End();

			foreach (var entity in filter)
			{
				ref var follower = ref followerPool.Get(entity);
				ref var move = ref movePool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf)
					continue;

				// Is the leader alive? EcsPackedEntity catches both death and reuse from the pool.
				if (!follower.Leader.Unpack(world, out int leaderEntity) || !mobPool.Has(leaderEntity))
				{
					Promote(world, entity, followerPool);
					continue;
				}

				ref var leaderMob = ref mobPool.Get(leaderEntity);
				if (leaderMob.Value == null || !leaderMob.Value.gameObject.activeSelf)
				{
					Promote(world, entity, followerPool);
					continue;
				}

				Transform leaderTransform = leaderMob.Value.transform;
				Transform selfTransform = mob.Value.transform;

				// §6: world slot relative to the leader's pose.
				Vector3 worldSlot = leaderTransform.position + leaderTransform.rotation * follower.SlotOffset;
				Vector3 self = selfTransform.position;
				worldSlot.y = self.y;

				Vector3 toSlot = worldSlot - self;
				toSlot.y = 0f;
				float dist = toSlot.magnitude;

				// §7c hysteresis: easier to stay in formation than to get into it.
				float tol = follower.InFormation ? Precision * 2f : Precision * 0.5f;

				if (dist <= tol)
				{
					follower.InFormation = true;
					move.Direction = Vector3.zero; // in the slot — stand still
					// Face toward the leader so the squad looks aligned.
					selfTransform.rotation = Quaternion.Slerp(
						selfTransform.rotation, leaderTransform.rotation, move.Speed * dt);
				}
				else
				{
					follower.InFormation = false;
					Vector3 dir = toSlot / dist;

					// §7c proportional controller: target speed = dist / FormationTime,
					// in fractions of base Speed (MoveDirect multiplies Speed by |Direction|).
					float wanted = dist / FormationTime;
					float frac = Mathf.Clamp(wanted / Mathf.Max(move.Speed, 0.01f), MinSpeedFrac, MaxSpeedFrac);
					move.Direction = dir * frac;

					selfTransform.rotation = Quaternion.Slerp(
						selfTransform.rotation, Quaternion.LookRotation(dir), move.Speed * dt);
				}
			}
		}

		/// <summary>
		/// Leader lost: remove from formation and return the mob to the regular chase after the player —
		/// add PathRecalculation + an out-of-turn path recalculation request.
		/// </summary>
		private static void Promote(EcsWorld world, int entity, EcsPool<FormationFollowerComponent> followerPool)
		{
			followerPool.Del(entity);

			var recalcPool = world.GetPool<PathRecalculation>();
			if (!recalcPool.Has(entity))
			{
				ref var recalc = ref recalcPool.Add(entity);
				recalc.Interval = world.GetAsSingleton<MainHolderComponent>().Value.PathRecalculationInterval;
				recalc.LastTime = 0f;
			}

			var requestPool = world.GetPool<PathRecalculationRequest>();
			if (!requestPool.Has(entity))
				requestPool.Add(entity);
		}
	}
}
