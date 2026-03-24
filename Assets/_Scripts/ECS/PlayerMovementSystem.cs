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
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();

			var filter = world.Filter<PlayerComponent>().Inc<PlayerInputComponent>().Inc<MoveComponent>().Inc<ModifierOwnerComponent>().End();
			foreach (var entity in filter)
			{
				ref var input = ref inputPool.Get(entity);

				Vector3 dir = new Vector3(input.Move.x, input.Move.y, input.Move.z);

				if (dir.sqrMagnitude > 0.01f)
				{
					ref var movement = ref movementPool.Get(entity);
					ref var player = ref playerPool.Get(entity);
					ref var modifierOwner = ref modifierPool.Get(entity);

					var speedMod = modifierOwner.GetModifier<SpeedModifier>();
					player.Value.transform.position += dir.normalized * movement.Speed * speedMod * Time.deltaTime;

				Debug.Log($"Move player {entity} with dir {dir} and speed {movement.Speed * speedMod} with mod {speedMod}");
				}
			}
		}
	}
}