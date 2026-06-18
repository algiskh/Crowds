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
			ref var inputActions = ref world.GetAsSingleton<InputActionsComponent>();

			inputActions.ActionMap = inputActions.Value.FindActionMap("Player");
			inputActions.MoveAction = inputActions.ActionMap.FindAction("Move", throwIfNotFound: true);
			inputActions.FireAction = inputActions.ActionMap.FindAction("Attack", throwIfNotFound: true);
			inputActions.MeleeAction = inputActions.ActionMap.FindAction("Melee", throwIfNotFound: true);
			inputActions.ReloadAction = inputActions.ActionMap.FindAction("Reload", throwIfNotFound: true);
			// Throw может отсутствовать, если .inputactions ещё не переимпортирован — не валим игру.
			inputActions.ThrowAction = inputActions.ActionMap.FindAction("Throw", throwIfNotFound: false);
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var input = ref world.GetAsSingleton<PlayerInputComponent>();

			#region Check pause / input lock
			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
			if (pauseState.IsPaused || inputLock.Locked)
			{
				input.Move = Vector3.zero;
				input.IsFiring = false;
				input.IsMeleeing = false;
				return;
			}
			#endregion

			input.PreviousMove = input.Move;

			ref var inputActions = ref world.GetAsSingleton<InputActionsComponent>();
			ref var cam = ref world.GetAsSingleton<CameraComponent>().Value;

			Vector2 moveInput = Vector2.zero;
			if (inputActions.MoveAction != null)
				moveInput = inputActions.MoveAction.ReadValue<Vector2>();

			bool isFiring = false;
			if (inputActions.FireAction != null)
			{
				isFiring = inputActions.FireAction.ReadValue<float>() > 0.5f;
			}

			if (inputActions.ReloadAction != null && inputActions.ReloadAction.triggered)
			{
				ref var requestReload = ref world.CreateSimpleEntity<RequestReloadComponent>();
			}

			bool isMeleeing = false;
			if (inputActions.MeleeAction != null && inputActions.MeleeAction.triggered)
			{
				if (input.MeleeCooldown > 0)
				{
					isMeleeing = false;
				}
				else
				{
					isMeleeing = true;
				}
			}

			if (input.MeleeCooldown > 0)
			{
				input.MeleeCooldown -= Time.deltaTime;
				if (input.MeleeCooldown < 0)
					input.MeleeCooldown = 0;
			}


			Vector3 moveDir = Vector3.zero;

			if (cam != null)
			{
				Vector3 camForward = cam.transform.forward;
				camForward.y = 0f;
				camForward.Normalize();

				Vector3 camRight = cam.transform.right;
				camRight.y = 0f;
				camRight.Normalize();

				moveDir = camForward * moveInput.y + camRight * moveInput.x;
				if (moveDir.sqrMagnitude > 1f)
					moveDir.Normalize();
			}
			else
			{
				moveDir = new Vector3(moveInput.x, 0f, moveInput.y);
			}

			input.Move = moveDir;
			input.IsFiring = isFiring;
			input.IsMeleeing = isMeleeing;
		}
	}
}
