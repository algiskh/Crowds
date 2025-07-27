using Leopotam.EcsLite;
using System.Runtime.Serialization.Json;
using UnityEngine;

namespace ECS
{
	public class MoveSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();

			foreach (var entity in world.Filter<MoveComponent>().End())
			{
				ref var moveComponent = ref movePool.Get(entity);

				if (pathPool.Has(entity))
				{
					MoveByPath(pathPool, entity, moveComponent);
				}
				else
				{
					MoveDirect(moveComponent);
				}
			}
		}

		#region MoveMethods
		/// <summary>
		/// Move agent by path if it exists
		/// </summary>
		private void MoveByPath(EcsPool<MovePath> pathPool, int entity, MoveComponent moveComponent)
		{
			ref var movePath = ref pathPool.Get(entity);

			if (movePath.Waypoints == null || movePath.Waypoints.Count == 0)
				return;

			var transform = moveComponent.Transform;
			Vector3 currentPosition = transform.position;
			Vector3 targetWaypoint = movePath.Waypoints[movePath.CurrentIndex];

			targetWaypoint.y = currentPosition.y;

			float moveSpeed = moveComponent.Speed;
			float step = moveSpeed * Time.deltaTime;

			if (Vector3.Distance(currentPosition, targetWaypoint) < 0.05f)
			{
				if (movePath.CurrentIndex < movePath.Waypoints.Count - 1)
				{
					movePath.CurrentIndex++;
					targetWaypoint = movePath.Waypoints[movePath.CurrentIndex];
				}
				else
				{
					pathPool.Del(entity);
					moveComponent.Direction = Vector3.zero;
					return;
				}
			}

			Vector3 dir = (targetWaypoint - currentPosition).normalized;
			moveComponent.Direction = dir;
			transform.position += dir * step;
		}

		/// <summary>
		/// Move agent directly in the direction specified in MoveComponent
		/// </summary>
		private void MoveDirect(MoveComponent moveComponent)
		{
			if (moveComponent.Transform == null)
			{
				Debug.LogWarning("MoveComponent has no Transform assigned.");
				return;
			}
			Debug.Log($"MoveDirect: {moveComponent.Direction} Speed: {moveComponent.Speed}");
			var move = moveComponent.Direction;
			moveComponent.Transform.position += moveComponent.Speed * Time.deltaTime * move;
		}
		#endregion
	}
}