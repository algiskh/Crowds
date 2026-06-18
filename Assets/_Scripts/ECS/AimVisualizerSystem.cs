using Leopotam.EcsLite;
using System;
using Unity.Burst.Intrinsics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ECS
{
	public sealed class AimVisualizerSystem : IEcsInitSystem, IEcsRunSystem
	{
		private const float GAMEPAD_CURSOR_SPEED = 3500f;
		private const float STICK_DEAD_ZONE_SQR = 0.01f;

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			// --- Looker init ---
			var lookerPool = world.GetPool<LookerAtCamera>();
			var aimVisualizerPool = world.GetPool<AimVisualizerComponent>();

			var filter = world.Filter<AimVisualizerComponent>().End();
			foreach (var entity in filter)
			{
				var aim = aimVisualizerPool.Get(entity);

				if (aim.Value.TryToGetLooker(out var looker))
				{
					var newEntity = world.NewEntity();
					ref var newLooker = ref lookerPool.Add(newEntity);
					newLooker = looker;
				}
			}


			// --- Virtual cursor init ---
			ref var cursor = ref world.CreateSimpleEntity<VirtualAimCursorComponent>();
			cursor.ScreenPosition = new Vector2(
				Screen.width * 0.5f,
				Screen.height * 0.5f
			);
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			ref var aimVisualizer = ref world.GetAsSingleton<AimVisualizerComponent>();
			ref var aimInput = ref world.GetAsSingleton<AimInputComponent>();
			ref var cursor = ref world.GetAsSingleton<VirtualAimCursorComponent>();

			bool isGamepad = CheckGamePad(ref aimInput);

			if (isGamepad)
			{
				if (aimInput.Value != Vector2.zero && aimInput.Value.sqrMagnitude >= STICK_DEAD_ZONE_SQR)
				{
					cursor.ScreenPosition +=
						GAMEPAD_CURSOR_SPEED * Time.deltaTime * aimInput.Value;

					cursor.ScreenPosition.x =
						Mathf.Clamp(cursor.ScreenPosition.x, 0f, Screen.width);

					cursor.ScreenPosition.y =
						Mathf.Clamp(cursor.ScreenPosition.y, 0f, Screen.height);
				}

				aimVisualizer.Value.SetAim(cursor.ScreenPosition);
			}
			else
			{
				Vector2 mousePos = Mouse.current.position.ReadValue();

				if (mousePos != cursor.PrevPosition)
				{
					cursor.ScreenPosition = mousePos;
					cursor.PrevPosition = mousePos;
				}
				aimVisualizer.Value.SetAim(cursor.ScreenPosition);
			}
		}


		private bool CheckGamePad(ref AimInputComponent aimInput)
		{
			var device = aimInput.AimAction.action.activeControl?.device;

			if (device is Gamepad)
			{
				aimInput.Value = aimInput.AimAction.action.ReadValue<Vector2>();
				return true;
			}

			return false;
		}
	}
}
