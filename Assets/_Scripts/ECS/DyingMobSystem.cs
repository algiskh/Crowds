using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Lets a dead mob's view play its "die" animation before it goes back to the pool. DamageSystem deletes the
	/// gameplay entity at the moment of death (loot, frag count, death effect happen there as usual) and, for mobs
	/// with <see cref="MobConfig.DeathAnimationTime"/> &gt; 0, hands the GameObject to a <see cref="DyingMobComponent"/>
	/// entity instead of deactivating it. When the timer runs out the view is pushed to the mob pool and disabled —
	/// exactly what DamageSystem does for an instant death. Runs right after DamageSystem.
	/// </summary>
	public sealed class DyingMobSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused)
				return;

			var dyingPool = world.GetPool<DyingMobComponent>();
			float dt = Time.deltaTime;

			foreach (var entity in world.Filter<DyingMobComponent>().End())
			{
				ref var dying = ref dyingPool.Get(entity);
				dying.Timer -= dt;
				if (dying.Timer > 0f && dying.Value != null)
					continue;

				if (dying.Value != null)
					ReturnToPool(world, dying.Value);
				world.DelEntity(entity);
			}
		}

		/// <summary>Pushes the mob view to its pool and disables it (shared with DamageSystem's instant death).</summary>
		public static void ReturnToPool(EcsWorld world, Mob mob)
		{
			ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
			if (mobPool.Pools == null)
				mobPool.Pools = new Dictionary<string, Stack<Mob>>();
			if (!mobPool.Pools.TryGetValue(mob.Id, out var stack))
			{
				stack = new Stack<Mob>();
				mobPool.Pools[mob.Id] = stack;
			}
			stack.Push(mob);
			mob.gameObject.SetActive(false);
		}
	}
}
