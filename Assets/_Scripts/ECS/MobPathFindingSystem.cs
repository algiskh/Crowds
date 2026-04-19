using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	public class MobPathfindingSystem : IEcsRunSystem
	{
		// Переиспользуемый буфер пути — один объект на всю систему.
		private readonly NavMeshPath _navPath = new NavMeshPath();
		private Vector3[] _cornersBuffer = new Vector3[32];

		// Пул List<Vector3> под waypoints, чтобы не аллоцировать при каждом пересчёте.
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

				if (NavMesh.CalculatePath(mobGO.transform.position, targetPos, NavMesh.AllAreas, _navPath)
					&& _navPath.status == NavMeshPathStatus.PathComplete)
				{
					int cornerCount = _navPath.GetCornersNonAlloc(_cornersBuffer);
					if (cornerCount == _cornersBuffer.Length)
					{
						// Буфер был переполнен — расширяемся и пересчитываем разово.
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
	}
}