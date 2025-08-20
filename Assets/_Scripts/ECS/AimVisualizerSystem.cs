using Leopotam.EcsLite;
using System.Diagnostics;

namespace ECS
{
	public class AimVisualizerSystem : IEcsInitSystem, IEcsRunSystem
	{
		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var lookerPool = world.GetPool<LookerAtCamera>();
			var aimVisualizerPool = world.GetPool<AimVisualizerComponent>();

			var filter = world.Filter<AimVisualizerComponent>().End();
			foreach (var entity in filter)
			{
				var aim = aimVisualizerPool.Get(entity);

				if (aim.Value.TryToGetLooker(out var looker))
				{
					UnityEngine.Debug.Log($"Add new looker. Looker transform is {looker.Transform != null}");
					var newEntity = world.NewEntity();
					ref var newLooker = ref lookerPool.Add(newEntity);
					newLooker = looker;
				}
			}
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region Check pause  
			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
			{
				return;
			}
			#endregion

			ref var aimVisualizer = ref world.GetAsSingleton<AimVisualizerComponent>();

			// Get cursor position on screen  
			var cursorPosition = UnityEngine.Input.mousePosition;

			aimVisualizer.Value.SetAim(cursorPosition);
		}
	}
}