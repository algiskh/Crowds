using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
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
				ref var requestPause = ref world.CreateSimpleEntity<RequestPauseComponent>();
				requestPause.Source = SignalSource.EndGame;

				ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
				requestOpenWindow.WindowType = endGameComponent.isWin ? WindowType.WinWindow : WindowType.FailWindow;
				StopAllMoves(world, playerComponent);
				world.DelEntity(entity);
			}
		}

		private void StopAllMoves(EcsWorld world, PlayerComponent player)
		{
			var moveSystemPool = world.GetPool<MoveComponent>();
			var mobPool = world.GetPool<MobComponent>();

			var moveFilter = world.Filter<MoveComponent>()
				.End();

			foreach (var entity in moveFilter)
			{
				ref var moveComponent = ref moveSystemPool.Get(entity);
				moveComponent.Speed = 0f; // Stop all movement
				moveComponent.Direction = Vector2.zero; // Reset direction

				//world.DelEntity(entity); // Optionally, remove the MoveComponent to prevent further processing
			}

			var mobFilter = world.Filter<MobComponent>()
				.End();
			foreach (var entity in mobFilter)
			{
				ref var mobComponent = ref mobPool.Get(entity);
				mobComponent.Value.Animator.Pause();
			}

			player.Value.Animator.Pause();
			ref var spawnRequest = ref world.GetAsSingleton<SpawnRequestComponent>();
			spawnRequest.IsBlocked = true;
		}
	}
}