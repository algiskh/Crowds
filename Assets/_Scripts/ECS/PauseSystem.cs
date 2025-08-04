using Leopotam.EcsLite;

namespace ECS
{
	public class PauseSystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var pauseComponent = ref world.CreateSimpleEntity<PauseStateComponent>();
			pauseComponent.IsPaused = false;
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var pauseComponent = ref world.GetAsSingleton<PauseStateComponent>();

			var requestPauseFilter = world.Filter<RequestPauseComponent>().End();
			var requestUnpauseFilter = world.Filter<RequestUnpauseComponent>().End();
			var pausePool = world.GetPool<RequestPauseComponent>();

			foreach (var entity in requestPauseFilter)
			{
				var request = pausePool.Get(entity);

				if (pauseComponent.IsPaused)
					continue;

				pauseComponent.IsPaused = true;
				pauseComponent.PreviousSource = request.Source;
			}

			foreach (var entity in requestUnpauseFilter)
			{
				var request = world.GetPool<RequestUnpauseComponent>().Get(entity);
				if (!pauseComponent.IsPaused && pauseComponent.PreviousSource <= request.Source)
					continue;
				pauseComponent.IsPaused = false;
				pauseComponent.PreviousSource = request.Source;
			}

			world.DeleteAllWith<RequestPauseComponent>();
			world.DeleteAllWith<RequestUnpauseComponent>();
		}
	}
}