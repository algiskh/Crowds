using Leopotam.EcsLite;

namespace ECS
{
	public class SmartConditionSystem : IEcsRunSystem
	{
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

			var smartConditionPool = world.GetPool<SmartConditionComponent>();
			var filter = world.Filter<SmartConditionComponent>().End();
			foreach (var entity in filter)
			{
				ref var smartCondition = ref smartConditionPool.Get(entity);
				if (smartCondition.Value == null)
				{
					continue;
				}
				// Always re-evaluate: "live" conditions (NoAmmoAround) must be able
				// to become false again, otherwise IsFulfilled sticks to true forever and
				// the observer spawns loot endlessly. Accumulator conditions
				// (FragsCondition) are protected by an internal guard and stay true.
				smartCondition.Value.Iterate();
			}
		}
	}
}
