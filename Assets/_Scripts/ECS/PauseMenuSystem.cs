using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ECS
{
	/// <summary>
	/// Pause menu: the "Pause" input action (Esc / gamepad Start) toggles <see cref="PauseMenu"/> and the game pause
	/// (RequestPause/RequestUnpause with <see cref="SignalSource.UI"/>, applied by PauseSystem the same frame —
	/// registered right before it). Doesn't open over another pause (fail/win windows) or during the fail sequence.
	/// Main menu: resets timeScale and loads <see cref="MainMenuScene"/>.
	/// </summary>
	public sealed class PauseMenuSystem : IEcsInitSystem, IEcsRunSystem
	{
		public const string MainMenuScene = "MainMenu";

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var menu = ref world.CreateSimpleEntity<PauseMenuComponent>();
			menu.Value = PauseMenu.Create();
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var menu = ref world.GetAsSingleton<PauseMenuComponent>();
			if (menu.Value == null)
				return;

			if (menu.Value.MainMenuRequested)
			{
				menu.Value.SetOpen(false);
				Time.timeScale = 1f;
				SceneManager.LoadScene(MainMenuScene);
				return;
			}

			bool toggle = PausePressed(world);
			if (menu.Value.IsOpen)
			{
				if (toggle || menu.Value.ContinueRequested)
				{
					menu.Value.SetOpen(false);
					ref var unpause = ref world.CreateSimpleEntity<RequestUnpauseComponent>();
					unpause.Source = SignalSource.UI;
				}
				return;
			}

			if (!toggle)
				return;
			bool paused = world.GetAsSingleton<PauseStateComponent>().IsPaused;
			bool failing = world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive;
			bool locked = world.TryGetAsSingleton<InputLockComponent>(out var inputLock) && inputLock.Locked;
			if (paused || failing || locked)
				return;

			menu.Value.SetOpen(true);
			ref var pause = ref world.CreateSimpleEntity<RequestPauseComponent>();
			pause.Source = SignalSource.UI;
		}

		private static bool PausePressed(EcsWorld world)
		{
			var action = world.GetAsSingleton<InputActionsComponent>().PauseAction;
			if (action != null)
				return action.WasPressedThisFrame();
			// The asset hasn't been reimported with the Pause action yet: plain Esc.
			return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
		}
	}
}
