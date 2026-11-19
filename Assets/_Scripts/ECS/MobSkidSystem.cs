using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Ice skid for mobs. On slippery ground (InertiaComponent.Sliding) a mob's real velocity can disagree with where
	/// it wants to go: it overshoots a turn, drifts sideways or keeps gliding after stopping. While that lasts the
	/// mob is "skidding": it faces the player and its view plays Skid (Idle if the view has no skid clip) through
	/// <see cref="AnimationStateComponent.Override"/>, so gameplay systems keep owning Requested.
	/// Attack/aim/shoot/throw/die are never overridden (telegraphs must stay readable).
	/// Runs after MoveSystem (its facing toward the waypoint is replaced) and before AnimationSystem/CrowdRenderSystem.
	/// See Docs/HockeyLevelFeature.md.
	/// </summary>
	public class MobSkidSystem : IEcsRunSystem
	{
		// Below this real speed the mob isn't gliding anywhere — no skid: a fraction of its own run speed
		// (mobs run 2..5 m/s), but never below the absolute floor.
		private const float MinSkidSpeedFraction = 0.4f;
		private const float MinSkidSpeedFloor = 0.8f;
		// Angle between real velocity and intended direction: enter above, leave below (hysteresis).
		private const float EnterAngle = 55f;
		private const float ExitAngle = 30f;
		// How long the condition must hold to enter / leave — no flicker on a single bumpy frame.
		private const float EnterDelay = 0.12f;
		private const float ExitDelay = 0.25f;
		// Turn speed toward the player while skidding, 1/s (Slerp factor per second).
		private const float FaceTurnSpeed = 10f;

		// Dedicated skid clip (e.g. the Fast zombie's Mixamo "Wobbling"); mobs whose view lacks it play Idle.
		private const AnimationType SkidAnimation = AnimationType.Skid;
		private const AnimationType SkidFallback = AnimationType.Idle;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			if (world.IsWorldFrozen())
				return;

			var mobPool = world.GetPool<MobComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var inertiaPool = world.GetPool<InertiaComponent>();
			var animPool = world.GetPool<AnimationStateComponent>();
			var crowdPool = world.GetPool<CrowdInstanceComponent>();

			bool hasPlayer = world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null;
			Vector3 playerPos = hasPlayer ? player.Value.transform.position : Vector3.zero;
			float dt = Time.deltaTime;
			float enterCos = Mathf.Cos(EnterAngle * Mathf.Deg2Rad);
			float exitCos = Mathf.Cos(ExitAngle * Mathf.Deg2Rad);

			foreach (var entity in world.Filter<MobComponent>().Inc<InertiaComponent>().Inc<MoveComponent>().End())
			{
				ref var mob = ref mobPool.Get(entity);
				if (mob.Value == null || !mob.Value.gameObject.activeSelf)
					continue;

				ref var inertia = ref inertiaPool.Get(entity);
				ref var move = ref movePool.Get(entity);

				bool hasAnim = animPool.Has(entity);
				AnimationType requested = hasAnim ? animPool.Get(entity).Requested : AnimationType.Run;
				bool overridable = requested == AnimationType.Run || requested == AnimationType.Walk || requested == AnimationType.Idle;

				// Off the ice or busy with an action: stop skidding right away.
				if (!inertia.Sliding || !overridable)
				{
					SetSkid(ref inertia, animPool, crowdPool, mob.Value, entity, false);
					continue;
				}

				Vector3 velocity = inertia.Velocity;
				velocity.y = 0f;
				float speed = velocity.magnitude;
				Vector3 desired = move.Direction;
				desired.y = 0f;

				// cos of the angle between where it moves and where it wants to go; no intent while still gliding = full skid.
				float cos = desired.sqrMagnitude > 1e-4f && speed > 1e-3f
					? Vector3.Dot(velocity / speed, desired.normalized)
					: -1f;

				float minSpeed = Mathf.Max(MinSkidSpeedFloor, MinSkidSpeedFraction * move.Speed);
				bool condition = inertia.Skidding
					? speed >= minSpeed * 0.5f && cos < exitCos   // keep skidding until clearly realigned / stopped
					: speed >= minSpeed && cos < enterCos;

				if (condition != inertia.Skidding)
				{
					inertia.SkidTimer += dt;
					if (inertia.SkidTimer >= (inertia.Skidding ? ExitDelay : EnterDelay))
						SetSkid(ref inertia, animPool, crowdPool, mob.Value, entity, !inertia.Skidding);
				}
				else
				{
					inertia.SkidTimer = 0f;
				}

				if (!inertia.Skidding)
					continue;

				// Keep the override applied (a gameplay system may have re-added the component this frame).
				SetSkid(ref inertia, animPool, crowdPool, mob.Value, entity, true);

				if (hasPlayer && move.Transform != null)
				{
					Vector3 toPlayer = playerPos - move.Transform.position;
					toPlayer.y = 0f;
					if (toPlayer.sqrMagnitude > 1e-4f)
						move.Transform.rotation = Quaternion.Slerp(move.Transform.rotation,
							Quaternion.LookRotation(toPlayer), FaceTurnSpeed * dt);
				}
			}
		}

		private static void SetSkid(ref InertiaComponent inertia, EcsPool<AnimationStateComponent> animPool,
			EcsPool<CrowdInstanceComponent> crowdPool, Mob mob, int entity, bool on)
		{
			if (inertia.Skidding != on)
				inertia.SkidTimer = 0f;
			inertia.Skidding = on;

			if (!animPool.Has(entity))
			{
				if (!on)
					return;
				// Same default as AnimationSystem: Run, applied on its next reconcile.
				ref var added = ref animPool.Add(entity);
				added.Requested = AnimationType.Run;
				added.HasCurrent = false;
				added.Restart = false;
			}

			ref var anim = ref animPool.Get(entity);
			// Resolve the clip once per skid (not every frame while it lasts).
			if (on && !anim.OverrideActive)
				anim.Override = ResolveSkidAnimation(mob, crowdPool, entity);
			anim.OverrideActive = on;
		}

		private static AnimationType ResolveSkidAnimation(Mob mob, EcsPool<CrowdInstanceComponent> crowdPool, int entity)
		{
			if (crowdPool.Has(entity))
			{
				var library = crowdPool.Get(entity).Library;
				return library != null && library.TryGetClip(SkidAnimation, out _) ? SkidAnimation : SkidFallback;
			}

			return mob != null && mob.Animator != null && mob.Animator.HasState(SkidAnimation) ? SkidAnimation : SkidFallback;
		}
	}
}
