using Leopotam.EcsLite;
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
			var modifierOwnerPool = world.GetPool<ModifierOwnerComponent>();

			foreach (var entity in world.Filter<MoveComponent>().Inc<ModifierOwnerComponent>().End())
			{
				ref var moveComponent = ref movePool.Get(entity);
				ref var modifierOwnerComponent = ref modifierOwnerPool.Get(entity);

				if (pathPool.Has(entity))
				{
					MoveByPath(pathPool, entity, moveComponent, modifierOwnerComponent);
				}
				else
				{
					MoveDirect(world, moveComponent, modifierOwnerComponent);
				}
			}
		}

		#region MoveMethods
		/// <summary>
		/// Move agent by path if it exists
		/// </summary>
		private void MoveByPath(EcsPool<MovePath> pathPool, int entity, MoveComponent moveComponent, ModifierOwnerComponent modifierOwnerComponent)
		{
			ref var movePath = ref pathPool.Get(entity);

			if (movePath.Waypoints == null || movePath.Waypoints.Count == 0)
				return;

			var transform = moveComponent.Transform;
			Vector3 currentPosition = transform.position;
			Vector3 targetWaypoint = movePath.Waypoints[movePath.CurrentIndex];

			targetWaypoint.y = currentPosition.y;

			var compositeModifier = modifierOwnerComponent.GetModifier<SpeedModifier>();

			float moveSpeed = moveComponent.Speed * compositeModifier;
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

			if (dir != Vector3.zero)
			{
				Quaternion targetRotation = Quaternion.LookRotation(dir);
				transform.rotation = Quaternion.Slerp(
					transform.rotation,
					targetRotation,
					moveComponent.Speed * Time.deltaTime
				);
			}

			transform.position += dir * step;
		}

		/// <summary>
		/// Move agent directly in the direction specified in MoveComponent
		/// </summary>
		private void MoveDirect(EcsWorld world, MoveComponent moveComponent, ModifierOwnerComponent modifierOwner)
		{
			if (moveComponent.Transform == null)
			{
				Debug.LogWarning("MoveComponent has no Transform assigned.");
				return;
			}
			//Debug.Log($"MoveDirect: {moveComponent.Direction} Speed: {moveComponent.Speed}");

			var move = moveComponent.Direction;

			var compositeModifier = modifierOwner.GetModifier<SpeedModifier>();
			moveComponent.Transform.position += moveComponent.Speed * compositeModifier * Time.deltaTime * move;
		}
		#endregion
	}
}