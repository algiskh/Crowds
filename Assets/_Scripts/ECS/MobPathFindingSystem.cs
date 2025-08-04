using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine.AI;
using UnityEngine;

namespace ECS
{
	public class MobPathfindingSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mobPool = world.GetPool<MobComponent>();
			ref var target = ref world.GetAsSingleton<PlayerComponent>();

			var pathPool = world.GetPool<MovePath>();
			var recalcPool = world.GetPool<PathRecalculation>();

			var filter = world.Filter<MobComponent>().Inc<PathRecalculation>().End();
			float now = Time.time;

			foreach (var entity in filter)
			{
				ref var mob = ref mobPool.Get(entity);
				ref var recalc = ref recalcPool.Get(entity);

				if (now - recalc.LastTime < recalc.Interval)
					continue;

				recalc.LastTime = now;

				var mobGO = mob.Value.gameObject;
				if (mobGO == null) continue;
				var targetPos = target.Value.transform.position;

				NavMeshPath navPath = new NavMeshPath();
				if (NavMesh.CalculatePath(mobGO.transform.position, targetPos, NavMesh.AllAreas, navPath)
					&& navPath.status == NavMeshPathStatus.PathComplete)
				{
					ref var movePath = ref pathPool.Has(entity)
						? ref pathPool.Get(entity)
						: ref pathPool.Add(entity);

					movePath.Waypoints = new List<Vector3>(navPath.corners);
					movePath.CurrentIndex = 0;
				}
				else
				{
					// Не удалось построить путь — можно удалить MovePath, залогировать ошибку
					if (pathPool.Has(entity))
						pathPool.Del(entity);

					UnityEngine.Debug.LogWarning(
						$"Can't build path for mob {mob.Value.name} to target {target.Value.name}, status: {navPath.status}, mobPos: {mobGO.transform.position}, targetPos: {targetPos}");
				}
			}
		}
	}
}