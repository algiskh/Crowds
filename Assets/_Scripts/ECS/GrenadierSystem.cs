using Leopotam.EcsLite;
using Scene.Animation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	/// <summary>
	/// Grenadier mob behavior. In every other respect it's a regular mob (MobComponent), but instead of
	/// going into melee it keeps its distance and throws grenades:
	///
	///  • Chase    — player beyond X: approaches via regular pathfinding (this system doesn't touch it).
	///  • Throw    — player in the [Y, X] band: stops, plays "throw", and when the windup
	///               (ThrowWindup) finishes throws a grenade at the player's current position.
	///  • Cooldown — after the throw stands for ThrowCooldown seconds (animation "throw_cooldown").
	///  • Flee     — player closer than Y: retreats to a free NavMesh spot away from the player.
	///
	/// Runs after MobPathfindingSystem and before MoveSystem: in non-standard states it
	/// overwrites/clears the path built by pathfinding toward the player, thereby controlling movement.
	/// The mob's grenade hits the player with its explosion (not mobs) — see GrenadeLauncher/ExplosionSystem.
	/// </summary>
	public sealed class GrenadierSystem : IEcsRunSystem
	{
		// Angles (degrees) for finding a free retreat point: first straight away from the player, then sideways.
		// Never beyond ±90°: the point always stays "on the mob's side" and the retreat doesn't lead through the player.
		private static readonly float[] _fleeAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
		// Tolerance: the retreat path may approach the player no closer than the current distance minus this.
		private const float FleeClearanceTolerance = 0.5f;
		private const float FleeArriveDistance = 0.4f;

		// Reusable buffers for computing the retreat path over the NavMesh.
		private readonly NavMeshPath _fleePath = new NavMeshPath();
		private Vector3[] _fleeCorners = new Vector3[16];
		private int _fleeCornerCount;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				return;
			Vector3 playerPos = playerSingleton.Value.transform.position;

			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;

			var grenadierPool = world.GetPool<GrenadierComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();
			var recalcRequestPool = world.GetPool<PathRecalculationRequest>();
			var animPool = world.GetPool<AnimationStateComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<GrenadierComponent>().Inc<MobComponent>().Inc<MoveComponent>().End();
			foreach (var entity in filter)
			{
				ref var grenadier = ref grenadierPool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf || grenadier.Config == null)
					continue;

				var config = grenadier.Config;
				Vector3 mobPos = mob.Value.transform.position;

				Vector3 flat = playerPos - mobPos;
				flat.y = 0f;
				float distance = flat.magnitude;

				switch (grenadier.State)
				{
					case GrenadierState.Chase:
						if (distance < config.ThrowMinDistance)
							EnterFlee(entity, ref grenadier, animPool);
						else if (distance <= config.ThrowMaxDistance)
							EnterThrow(entity, ref grenadier, mob, mobPos, playerPos, animPool);
						// otherwise we stay in Chase — movement toward the player is handled by MobPathfindingSystem/MoveSystem.
						break;

					case GrenadierState.Throw:
						StopMovement(entity, movePool, pathPool);
						grenadier.Timer -= dt;
						if (grenadier.Timer <= 0f)
						{
							GrenadeLauncher.Launch(world, mainHolder, config.GrenadeConfig,
								mobPos, playerPos);
							EnterCooldown(entity, ref grenadier, animPool);
						}
						break;

					case GrenadierState.Cooldown:
						StopMovement(entity, movePool, pathPool);
						grenadier.Timer -= dt;
						if (grenadier.Timer <= 0f)
							EnterChase(entity, ref grenadier, recalcRequestPool, animPool);
						break;

					case GrenadierState.Flee:
						if (distance >= config.ThrowMinDistance)
						{
							// Retreated far enough — throw again (if in the band) or catch up.
							if (distance <= config.ThrowMaxDistance)
								EnterThrow(entity, ref grenadier, mob, mobPos, playerPos, animPool);
							else
								EnterChase(entity, ref grenadier, recalcRequestPool, animPool);
						}
						else
						{
							FleeStep(entity, ref grenadier, mobPos, playerPos, config.ThrowMinDistance, movePool, pathPool);
						}
						break;
				}
			}
		}

		#region State transitions

		private void EnterChase(int entity, ref GrenadierComponent grenadier,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Chase;
			grenadier.HasFleeTarget = false;
			RequestAnimation(animPool, entity, AnimationType.Run);

			// Make pathfinding build a path to the player right away, not wait for the interval.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		private void EnterThrow(int entity, ref GrenadierComponent grenadier, in MobComponent mob,
			Vector3 mobPos, Vector3 playerPos, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Throw;
			grenadier.Timer = grenadier.Config.ThrowWindup;
			grenadier.HasFleeTarget = false;
			FacePlayer(mob, mobPos, playerPos);
			RequestAnimation(animPool, entity, AnimationType.Throw);
		}

		private void EnterCooldown(int entity, ref GrenadierComponent grenadier, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Cooldown;
			grenadier.Timer = grenadier.Config.ThrowCooldown;
			RequestAnimation(animPool, entity, AnimationType.ThrowCooldown);
		}

		private void EnterFlee(int entity, ref GrenadierComponent grenadier, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Flee;
			grenadier.HasFleeTarget = false;
			RequestAnimation(animPool, entity, AnimationType.Run);
		}

		#endregion

		#region Movement helpers

		/// <summary>
		/// Full stop: kills the direction and clears the path to the player built by pathfinding
		/// (waypoints are cleared, but the path component isn't deleted — the list is reused by pathfinding).
		/// </summary>
		private void StopMovement(int entity, EcsPool<MoveComponent> movePool, EcsPool<MovePath> pathPool)
		{
			ref var move = ref movePool.Get(entity);
			move.Direction = Vector3.zero;

			if (pathPool.Has(entity))
			{
				ref var path = ref pathPool.Get(entity);
				path.Waypoints?.Clear();
			}
		}

		/// <summary>
		/// Retreat step: picks (if needed) a free NavMesh spot away from the player and leads the
		/// mob to it along a NavMesh path (path corners — waypoints). Pathfinding periodically overwrites
		/// MovePath with a path to the player — then the retreat path is rebuilt from the current position.
		/// </summary>
		private void FleeStep(int entity, ref GrenadierComponent grenadier, Vector3 mobPos, Vector3 playerPos,
			float minDistance, EcsPool<MoveComponent> movePool, EcsPool<MovePath> pathPool)
		{
			bool needNew = !grenadier.HasFleeTarget;
			if (!needNew)
			{
				Vector3 toTarget = grenadier.FleeTarget - mobPos;
				toTarget.y = 0f;
				if (toTarget.sqrMagnitude < FleeArriveDistance * FleeArriveDistance)
					needNew = true; // reached the picked point but the player is still close — look for a new one.
			}

			bool hasFreshPath = false;
			if (needNew)
			{
				grenadier.HasFleeTarget = TryFindFleeTarget(mobPos, playerPos, minDistance, out var target);
				if (grenadier.HasFleeTarget)
				{
					grenadier.FleeTarget = target;
					hasFreshPath = true; // TryFindFleeTarget left a verified path in _fleeCorners.
				}
			}

			if (!grenadier.HasFleeTarget)
			{
				// No free spot found — just stand still instead of crowding the player.
				StopMovement(entity, movePool, pathPool);
				return;
			}

			// The path already leads to the retreat point and MoveSystem is following it — don't touch (we'd reset progress).
			if (!hasFreshPath && IsFollowingFleePath(entity, grenadier.FleeTarget, pathPool))
				return;

			if (!hasFreshPath && !TryCalculateFleePath(mobPos, grenadier.FleeTarget, playerPos))
			{
				// The old route is no good (blocked or leads to the player) — pick a point again next frame.
				grenadier.HasFleeTarget = false;
				StopMovement(entity, movePool, pathPool);
				return;
			}

			ref var path = ref pathPool.Has(entity) ? ref pathPool.Get(entity) : ref pathPool.Add(entity);
			if (path.Waypoints == null)
				path.Waypoints = new List<Vector3>(_fleeCorners.Length);
			path.Waypoints.Clear();
			for (int i = 0; i < _fleeCornerCount; i++)
				path.Waypoints.Add(_fleeCorners[i]);
			path.CurrentIndex = 0;
		}

		private static bool IsFollowingFleePath(int entity, Vector3 fleeTarget, EcsPool<MovePath> pathPool)
		{
			if (!pathPool.Has(entity))
				return false;
			ref var path = ref pathPool.Get(entity);
			if (path.Waypoints == null || path.Waypoints.Count == 0)
				return false;
			Vector3 last = path.Waypoints[path.Waypoints.Count - 1];
			last.y = fleeTarget.y;
			return (last - fleeTarget).sqrMagnitude < 0.01f;
		}

		/// <summary>
		/// Finds a valid NavMesh point at a distance >= minDistance from the player, retreating away from it;
		/// when a direction is blocked, tries angles sideways (within ±90° of "away from the player").
		/// A point is accepted only if there's a complete NavMesh path to it that doesn't bring the mob closer to the player.
		/// </summary>
		private bool TryFindFleeTarget(Vector3 mobPos, Vector3 playerPos, float minDistance, out Vector3 result)
		{
			Vector3 away = mobPos - playerPos;
			away.y = 0f;
			if (away.sqrMagnitude < 0.0001f)
				away = Vector3.forward;
			away.Normalize();

			float desired = minDistance + 1.5f;
			for (int i = 0; i < _fleeAngles.Length; i++)
			{
				Vector3 dir = Quaternion.Euler(0f, _fleeAngles[i], 0f) * away;
				Vector3 candidate = playerPos + dir * desired;
				if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas))
					continue;

				Vector3 flat = hit.position - playerPos;
				flat.y = 0f;
				if (flat.sqrMagnitude < minDistance * minDistance)
					continue;

				if (TryCalculateFleePath(mobPos, hit.position, playerPos))
				{
					result = hit.position;
					return true;
				}
			}

			result = mobPos;
			return false;
		}

		/// <summary>
		/// Computes the NavMesh path to the retreat point into _fleeCorners. The path is rejected if it's
		/// incomplete or any of its segments passes closer to the player than the mob currently stands (minus tolerance).
		/// </summary>
		private bool TryCalculateFleePath(Vector3 from, Vector3 to, Vector3 playerPos)
		{
			_fleeCornerCount = 0;
			if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _fleePath)
				|| _fleePath.status != NavMeshPathStatus.PathComplete)
				return false;

			int cornerCount = _fleePath.GetCornersNonAlloc(_fleeCorners);
			if (cornerCount == _fleeCorners.Length)
			{
				_fleeCorners = new Vector3[_fleeCorners.Length * 2];
				cornerCount = _fleePath.GetCornersNonAlloc(_fleeCorners);
			}
			if (cornerCount == 0)
				return false;

			Vector2 player = new Vector2(playerPos.x, playerPos.z);
			float clearance = Mathf.Max(0f, Vector2.Distance(new Vector2(from.x, from.z), player) - FleeClearanceTolerance);
			for (int i = 0; i < cornerCount - 1; i++)
			{
				Vector2 a = new Vector2(_fleeCorners[i].x, _fleeCorners[i].z);
				Vector2 b = new Vector2(_fleeCorners[i + 1].x, _fleeCorners[i + 1].z);
				if (DistanceToSegment(player, a, b) < clearance)
					return false;
			}

			_fleeCornerCount = cornerCount;
			return true;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float lenSq = ab.sqrMagnitude;
			float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
			return Vector2.Distance(p, a + ab * t);
		}

		private void FacePlayer(in MobComponent mob, Vector3 mobPos, Vector3 playerPos)
		{
			Vector3 dir = playerPos - mobPos;
			dir.y = 0f;
			if (dir.sqrMagnitude > 0.0001f)
				mob.Value.transform.rotation = Quaternion.LookRotation(dir.normalized);
		}

		// Sets the desired animation on the mob's state component; AnimationSystem applies it to the view.
		private void RequestAnimation(EcsPool<AnimationStateComponent> animPool, int entity, AnimationType type)
		{
			ref var anim = ref animPool.Has(entity) ? ref animPool.Get(entity) : ref animPool.Add(entity);
			anim.Requested = type;
		}

		#endregion
	}
}
