using Leopotam.EcsLite;

namespace ECS
{
	/// <summary>
	/// Reaction to game end (EndGameComponent). Victory — stop and show the win window right away.
	/// Defeat — start the cinematic fail sequence (FailSequenceSystem): control lock → red
	/// veil → menu+pause, 0.5s per phase.
	/// </summary>
	public class CheckEndSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var playerComponent = ref world.GetAsSingleton<PlayerComponent>();
			var endGamePool = world.GetPool<EndGameComponent>();
			var endGameFilter = world.Filter<EndGameComponent>()
				.End();

			// Defeat takes priority over victory: if both outcomes arrive this frame or the defeat sequence
			// is already running (it lasts ~1s until the window), we ignore the victory — otherwise both windows would open.
			ref var failSequence = ref world.GetAsSingleton<FailSequenceComponent>();
			bool isFailing = failSequence.Phase != FailSequencePhase.Inactive;
			foreach (var entity in endGameFilter)
			{
				if (!endGamePool.Get(entity).isWin)
				{
					isFailing = true;
					break;
				}
			}

			foreach (var entity in endGameFilter)
			{
				var endGameComponent = endGamePool.Get(entity);

				if (endGameComponent.isWin)
				{
					if (isFailing)
					{
						world.DelEntity(entity);
						continue;
					}

					ref var requestPause = ref world.CreateSimpleEntity<RequestPauseComponent>();
					requestPause.Source = SignalSource.EndGame;

					ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
					requestOpenWindow.WindowType = WindowType.WinWindow;

					GameOverActions.StopAllMoves(world, playerComponent);
				}
				else
				{
					// Defeat — start the cinematic fail sequence (if not already running).
					ref var seq = ref world.GetAsSingleton<FailSequenceComponent>();
					if (seq.Phase == FailSequencePhase.Inactive)
					{
						seq.Phase = FailSequencePhase.BlockControls;
						seq.Timer = FailSequenceSystem.PhaseDuration;

						ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
						inputLock.Locked = true;

						GameOverActions.HideMobHealthBars(world);
					}
				}

				world.DelEntity(entity);
			}
		}
	}
}
