using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class CheckFailSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var endGameFilter = world.Filter<EndGameComponent>()
				.End();
			if (endGameFilter.GetEntitiesCount() > 0)
			{
				return;
			}

			var playerPool = world.GetPool<PlayerComponent>();
			var muzzlePool = world.GetPool<MuzzleComponent>();
			var healthPool = world.GetPool<HealthComponent>();


			var filter = world.Filter<PlayerComponent>()
				.Inc<MuzzleComponent>()
				.Inc<HealthComponent>()
				.End();

			foreach (var entity in filter)
			{
				ref var muzzle = ref muzzlePool.Get(entity);
				ref var health = ref healthPool.Get(entity);
				ref var player = ref playerPool.Get(entity);
				if (muzzle.Count <= 0 || health.CurrentHealth <= 0)
				{
					ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
					requestOpenWindow.WindowType = WindowType.FailWindow;
					StopAllMoves(world, player);
					var endGame = world.CreateSimpleEntity<EndGameComponent>();
				}
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
			}

			var mobFilter = world.Filter<MobComponent>()
				.End();
			foreach (var entity in mobFilter)
			{
				ref var mobComponent = ref mobPool.Get(entity);
				mobComponent.Value.SimpleAnimator.Stop();
			}

			player.Value.Animator.Pause();
			ref var spawnRequest = ref world.GetAsSingleton<SpawnRequestComponent>();
			spawnRequest.IsBlocked = true;
		}
	}
}