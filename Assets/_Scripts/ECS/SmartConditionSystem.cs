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
				// Всегда переоцениваем: «живые» условия (NoAmmoAround) должны уметь
				// снова стать false, иначе IsFulfilled залипает в true навсегда и
				// observer бесконечно доспаунивает лут. Условия-аккумуляторы
				// (FragsCondition) сами защищены внутренним guard'ом и остаются true.
				smartCondition.Value.Iterate();
			}
		}
	}
}