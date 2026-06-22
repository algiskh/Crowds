using Leopotam.EcsLite;

namespace ECS
{
	// Dispatches RequestUILogComponent messages to the UI log view, then clears the requests.
	// Mirrors UISystem's request-driven pattern; runs right after it.
	public class UILogSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var filter = world.Filter<RequestUILogComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;

			if (!world.TryGetAsSingleton(out UILogViewComponent logView) || logView.Value == null)
			{
				world.DeleteAllWith<RequestUILogComponent>();
				return;
			}

			var pool = world.GetPool<RequestUILogComponent>();
			foreach (var entity in filter)
			{
				ref var request = ref pool.Get(entity);
				if (!string.IsNullOrEmpty(request.Message))
					logView.Value.AddEntry(request.Message);
			}

			world.DeleteAllWith<RequestUILogComponent>();
		}
	}
}
