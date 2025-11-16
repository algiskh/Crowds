using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class AnimationSystem : IEcsSystem, IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mobsPool = world.GetPool<MobComponent>();
			var mainMobPool = world.GetAsSingleton<MobPoolComponent>();

			foreach (var entity in world.Filter<MobComponent>().End())
			{
				ref var mobComponent = ref mobsPool.Get(entity);

				if (mobComponent.Value.Animator == null)
					continue;

				var animator = mobComponent.Value.Animator;


				if (!animator.HasActiveAnimation && mobComponent.Value.gameObject.activeSelf)
					animator.SetAnimation("run");
			}
		}
	}
}