using Leopotam.EcsLite;

namespace ECS
{
	/// <summary>
	/// Реакция на конец игры (EndGameComponent). Победа — останавливаемся и сразу показываем окно победы.
	/// Поражение — запускаем кинематографичную концовку (FailSequenceSystem): блок управления → красная
	/// пелена → меню+пауза, по 0.5с на фазу.
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

			foreach (var entity in endGameFilter)
			{
				var endGameComponent = endGamePool.Get(entity);

				if (endGameComponent.isWin)
				{
					ref var requestPause = ref world.CreateSimpleEntity<RequestPauseComponent>();
					requestPause.Source = SignalSource.EndGame;

					ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
					requestOpenWindow.WindowType = WindowType.WinWindow;

					GameOverActions.StopAllMoves(world, playerComponent);
				}
				else
				{
					// Поражение — запускаем кинематографичную концовку (если ещё не идёт).
					ref var seq = ref world.GetAsSingleton<FailSequenceComponent>();
					if (seq.Phase == FailSequencePhase.Inactive)
					{
						seq.Phase = FailSequencePhase.BlockControls;
						seq.Timer = FailSequenceSystem.PhaseDuration;

						ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
						inputLock.Locked = true;
					}
				}

				world.DelEntity(entity);
			}
		}
	}
}
