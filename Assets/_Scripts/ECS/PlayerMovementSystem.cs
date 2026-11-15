using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class PlayerMovementSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var inputPool = world.GetPool<PlayerInputComponent>();
			var movementPool = world.GetPool<MoveComponent>();
			var playerPool = world.GetPool<PlayerComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var inertiaPool = world.GetPool<InertiaComponent>();
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();

			var filter = world.Filter<PlayerComponent>().Inc<PlayerInputComponent>().Inc<MoveComponent>().Inc<ModifierOwnerComponent>().End();
			foreach (var entity in filter)
			{
				ref var input = ref inputPool.Get(entity);

				Vector3 dir = new Vector3(input.Move.x, input.Move.y, input.Move.z);
				bool hasInput = dir.sqrMagnitude > 0.01f;
				ref var modifierOwner = ref modifierPool.Get(entity);
				// Ice (TractionModifier): inertial movement — keeps sliding without input, turns/stops slowly.
				float grip = Inertia.GetGrip(modifierOwner);
				bool inertial = !float.IsPositiveInfinity(grip) || inertiaPool.Has(entity);
				if (!hasInput && !inertial)
					continue;

				ref var movement = ref movementPool.Get(entity);
				ref var player = ref playerPool.Get(entity);
				var speedMod = modifierOwner.GetModifier<SpeedModifier>();
				Vector3 desired = hasInput ? dir.normalized * movement.Speed * speedMod : Vector3.zero;
				var playerTransform = player.Value.transform;
				playerTransform.position += inertial
					? Inertia.Displacement(inertiaPool, entity, playerTransform.position, desired, grip, Time.deltaTime)
					: desired * Time.deltaTime;
			}
		}
	}
}