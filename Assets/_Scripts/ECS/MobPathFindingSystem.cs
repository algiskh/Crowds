using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	public class MobPathfindingSystem : IEcsRunSystem
	{
		// Reusable path buffer — one object for the whole system.
		private readonly NavMeshPath _navPath = new NavMeshPath();
		private Vector3[] _cornersBuffer = new Vector3[32];

		// A pool of List<Vector3> for waypoints, to avoid allocating on every recalculation.
		private readonly Stack<List<Vector3>> _waypointListPool = new Stack<List<Vector3>>();

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mobPool = world.GetPool<MobComponent>();
			var pathPool = world.GetPool<MovePath>();
			var recalcPool = world.GetPool<PathRecalculation>();
			var requestPool = world.GetPool<PathRecalculationRequest>();

			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				return;

			var targetPos = playerSingleton.Value.transform.position;
			float now = Time.time;

			// Player down + other targets alive (escaping cheerleaders): every mob goes for the nearest one instead.
			var altTargetPool = world.GetPool<ZombieTargetComponent>();
			var altTargetFilter = world.Filter<ZombieTargetComponent>().End();
			bool useAltTargets = altTargetFilter.GetEntitiesCount() > 0
			                     && world.TryGetAsSingleton<FailSequenceComponent>(out var fail)
			                     && fail.Phase != FailSequencePhase.Inactive;

			var filter = world.Filter<MobComponent>().Inc<PathRecalculation>().End();
			foreach (var entity in filter)
			{
				ref var recalc = ref recalcPool.Get(entity);
				bool forced = requestPool.Has(entity);

				if (!forced && now - recalc.LastTime < recalc.Interval)
					continue;

				ref var mob = ref mobPool.Get(entity);
				var mobGO = mob.Value != null ? mob.Value.gameObject : null;
				if (mobGO == null)
				{
					if (forced) requestPool.Del(entity);
					continue;
				}

				recalc.LastTime = now;
				if (forced) requestPool.Del(entity);

				var mobTarget = useAltTargets
					? NearestTarget(altTargetPool, altTargetFilter, mobGO.transform.position, targetPos)
					: targetPos;
				if (NavMesh.CalculatePath(mobGO.transform.position, mobTarget, NavMesh.AllAreas, _navPath)
					&& _navPath.status == NavMeshPathStatus.PathComplete)
				{
					int cornerCount = _navPath.GetCornersNonAlloc(_cornersBuffer);
					if (cornerCount == _cornersBuffer.Length)
					{
						// The buffer was full — grow it and recalculate once.
						_cornersBuffer = new Vector3[_cornersBuffer.Length * 2];
						cornerCount = _navPath.GetCornersNonAlloc(_cornersBuffer);
					}

					ref var movePath = ref pathPool.Has(entity)
						? ref pathPool.Get(entity)
						: ref pathPool.Add(entity);

					List<Vector3> waypoints = movePath.Waypoints;
					if (waypoints == null)
						waypoints = _waypointListPool.Count > 0 ? _waypointListPool.Pop() : new List<Vector3>(16);
					waypoints.Clear();
					for (int i = 0; i < cornerCount; i++)
						waypoints.Add(_cornersBuffer[i]);

					movePath.Waypoints = waypoints;
					movePath.CurrentIndex = 0;
				}
				else if (pathPool.Has(entity))
				{
					ref var movePath = ref pathPool.Get(entity);
					if (movePath.Waypoints != null)
					{
						movePath.Waypoints.Clear();
						_waypointListPool.Push(movePath.Waypoints);
						movePath.Waypoints = null;
					}
					pathPool.Del(entity);
				}
			}
		}

		private static Vector3 NearestTarget(EcsPool<ZombieTargetComponent> pool, EcsFilter filter, Vector3 from, Vector3 fallback)
		{
			float best = float.MaxValue;
			Vector3 result = fallback;
			foreach (var entity in filter)
			{
				var t = pool.Get(entity).Value;
				if (t == null)
					continue;
				float d = (t.position - from).sqrMagnitude;
				if (d < best)
				{
					best = d;
					result = t.position;
				}
			}
			return result;
		}
	}
}
