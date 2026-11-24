using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class MoveSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			if (world.IsWorldFrozen())
				return;
			// Clamped like the player's: a hitch must not throw sliding mobs through each other / off the path.
			float dt = world.MoveDeltaTime();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();
			var modifierOwnerPool = world.GetPool<ModifierOwnerComponent>();
			var inertiaPool = world.GetPool<InertiaComponent>();
			var playerPool = world.GetPool<PlayerComponent>();

			foreach (var entity in world.Filter<MoveComponent>().Inc<ModifierOwnerComponent>().End())
			{
				ref var moveComponent = ref movePool.Get(entity);
				ref var modifierOwnerComponent = ref modifierOwnerPool.Get(entity);

				if (pathPool.Has(entity))
				{
					MoveByPath(pathPool, inertiaPool, entity, ref moveComponent, modifierOwnerComponent, dt);
				}
				else
				{
					MoveDirect(inertiaPool, entity, playerPool.Has(entity), moveComponent, modifierOwnerComponent, dt);
				}
			}
		}

		#region MoveMethods
		/// <summary>
		/// Move agent by path if it exists
		/// </summary>
		private void MoveByPath(EcsPool<MovePath> pathPool, EcsPool<InertiaComponent> inertiaPool, int entity,
			ref MoveComponent moveComponent, ModifierOwnerComponent modifierOwnerComponent, float dt)
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
			float step = moveSpeed * dt;

			// On ice (TractionModifier) the mob slides past waypoints — accept them from further away.
			float grip = Inertia.GetGrip(modifierOwnerComponent);
			float arriveDistance = float.IsPositiveInfinity(grip) ? 0.05f : 0.75f;

			if (Vector3.Distance(currentPosition, targetWaypoint) < arriveDistance)
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
					moveComponent.Speed * dt
				);
			}

			transform.position += float.IsPositiveInfinity(grip) && !inertiaPool.Has(entity)
				? dir * step
				: Inertia.Displacement(inertiaPool, entity, currentPosition, dir * moveSpeed, grip, dt);
		}

		/// <summary>
		/// Move agent directly in the direction specified in MoveComponent
		/// </summary>
		private void MoveDirect(EcsPool<InertiaComponent> inertiaPool, int entity, bool isPlayer, MoveComponent moveComponent,
			ModifierOwnerComponent modifierOwner, float dt)
		{
			if (moveComponent.Transform == null)
			{
				Debug.LogWarning("MoveComponent has no Transform assigned.");
				return;
			}
			//Debug.Log($"MoveDirect: {moveComponent.Direction} Speed: {moveComponent.Speed}");

			var move = moveComponent.Direction;

			var compositeModifier = modifierOwner.GetModifier<SpeedModifier>();
			Vector3 velocity = moveComponent.Speed * compositeModifier * move;
			// Mobs on ice keep sliding even without a path. The player's inertia is owned by PlayerMovementSystem —
			// never touch its InertiaComponent here (resetting it every frame left the player stuck on the ice).
			if (isPlayer)
			{
				moveComponent.Transform.position += velocity * dt;
				return;
			}
			float grip = Inertia.GetGrip(modifierOwner);
			moveComponent.Transform.position += float.IsPositiveInfinity(grip) && !inertiaPool.Has(entity)
				? velocity * dt
				: Inertia.Displacement(inertiaPool, entity, moveComponent.Transform.position, velocity, grip, dt);
		}
		#endregion
	}
}