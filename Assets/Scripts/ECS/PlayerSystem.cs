using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class PlayerSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var playerPool = world.GetPool<PlayerComponent>();
			ref var playerInput = ref world.GetAsSingleton<PlayerInputComponent>();
			Vector3 input = playerInput.Move;
			Vector3 previousInput = playerInput.PreviousMove;
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();

			var filter = world.Filter<PlayerComponent>().End();
			foreach (var entity in filter)
			{
				ref var player = ref playerPool.Get(entity);
				// Состояние Idle определяется по обеим осям (X и Z)
				bool isIdle = Mathf.Approximately(input.x, 0f) && Mathf.Approximately(input.z, 0f);

				// Fire input
				var fireRequestPool = world.GetPool<RequestFireComponent>();
				if (playerInput.IsFiring)
				{
					Debug.Log($"Try to fire");
					var fireEntity = world.NewEntity();
					ref var requestFireComponent = ref fireRequestPool.Add(fireEntity);
				}
			}
		}

		private void SetRun(ref PlayerComponent player, ref PlayerInputComponent playerInput)
		{
			//if (player.State != PlayerState.Run)
			//{
			//	player.Value.Animator.SetAnimation("Run");
			//	player.State = PlayerState.Run;
			//}
		}
	}
}
