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

			var filter = world.Filter<PlayerComponent>().Inc<PlayerInputComponent>().Inc<MoveComponent>().End();
			foreach (var entity in filter)
			{
				ref var input = ref inputPool.Get(entity);
				ref var movement = ref movementPool.Get(entity);
				ref var player = ref playerPool.Get(entity);

				Vector3 dir = new Vector3(input.Move.x, input.Move.y, input.Move.z);
				if (dir.sqrMagnitude > 0.01f)
				{
					player.Value.transform.position += dir.normalized * movement.Speed * Time.deltaTime;
				}
			}
		}
	}
}