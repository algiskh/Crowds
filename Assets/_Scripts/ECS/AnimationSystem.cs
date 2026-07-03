using Leopotam.EcsLite;
using Scene.Animation;

namespace ECS
{
	/// <summary>
	/// Single source of truth for mob animation: reconciles each mob's requested animation state
	/// against what's currently applied, and pushes to the Animator view only when it changes.
	/// Gameplay systems (e.g. GrenadierSystem) set <see cref="AnimationStateComponent.Requested"/>;
	/// they never touch the Animator directly. Mobs without the component default to Run.
	/// </summary>
	public class AnimationSystem : IEcsSystem, IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mobPool = world.GetPool<MobComponent>();
			var animPool = world.GetPool<AnimationStateComponent>();
			var crowdPool = world.GetPool<CrowdInstanceComponent>();

			foreach (var entity in world.Filter<MobComponent>().End())
			{
				// Crowd mobs have no live Animator — CrowdRenderSystem reconciles their animation instead.
				if (crowdPool.Has(entity))
					continue;

				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || mob.Value.Animator == null)
					continue;

				if (!mob.Value.gameObject.activeSelf)
					continue;

				ref var anim = ref animPool.Has(entity) ? ref animPool.Get(entity) : ref AddDefault(animPool, entity);

				if (!anim.HasCurrent || anim.Requested != anim.Current)
				{
					mob.Value.Animator.SetAnimation(anim.Requested);
					anim.Current = anim.Requested;
					anim.HasCurrent = true;
				}
			}
		}

		private static ref AnimationStateComponent AddDefault(EcsPool<AnimationStateComponent> pool, int entity)
		{
			ref var anim = ref pool.Add(entity);
			anim.Requested = AnimationType.Run; // mobs run by default; HasCurrent stays false so it applies this frame
			return ref anim;
		}
	}
}
