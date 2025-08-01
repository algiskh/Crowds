using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class InputSystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var playerInput = ref world.GetAsSingleton<PlayerInputComponent>();
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var input = ref world.GetAsSingleton<PlayerInputComponent>();

			var endGameFilter = world.Filter<EndGameComponent>().End();
			if (endGameFilter.GetEntitiesCount() > 0)
			{
				input.Move = Vector3.zero;
				return;
			}

			// Сохраняем предыдущее значение ДО вычисления нового
			input.PreviousMove = input.Move;

			ref var cam = ref world.GetAsSingleton<CameraComponent>().Value;
			float horizontal = Input.GetAxisRaw("Horizontal"); // A/D
			float vertical = Input.GetAxisRaw("Vertical");     // W/S
			Vector3 moveDir = Vector3.zero;

			if (cam != null)
			{
				// Направления камеры по XZ
				Vector3 camForward = cam.transform.forward;
				camForward.y = 0f;
				camForward.Normalize();

				Vector3 camRight = cam.transform.right;
				camRight.y = 0f;
				camRight.Normalize();

				moveDir = (camForward * vertical + camRight * horizontal);
				if (moveDir.sqrMagnitude > 1f)
					moveDir.Normalize();
			}

			input.Move = moveDir;
			input.IsFiring = Input.GetButton("Fire1");
		}
	}
}
