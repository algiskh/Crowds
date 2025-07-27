using Leopotam.EcsLite;

namespace ECS
{
	public class UISystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var muzzle = ref world.GetAsSingleton<MuzzleComponent>();
			ref var ammoCounter = ref world.GetAsSingleton<AmmoCounterComponent>();


			ammoCounter.Value.SetAmmo(muzzle.Count);


			var requestPool = world.GetPool<RequestOpenWindowComponent>();
			var filter = world.Filter<RequestOpenWindowComponent>()
				.End();
			foreach (var requestEntity in filter)
			{
				ref var request = ref requestPool.Get(requestEntity);
				if (request.WindowType is WindowType.FailWindow)
				{
					ref var failWindow = ref world.GetAsSingleton<FailWindowComponent>();
					failWindow.Value.Open();
				}
				world.DelEntity(requestEntity);
			}

			world.DeleteAllWith<RequestOpenWindowComponent>();	

		}
	}
}