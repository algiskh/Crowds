using Leopotam.EcsLite;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class EffectsSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var effectsHolder = world.GetAsSingleton<EffectsHolderComponent>();
			ref var effectMainPool = ref world.GetAsSingleton<EffectPoolComponent>();
			var effectPool = world.GetPool<EffectComponent>();
			var requetEffectsPool = world.GetPool<RequestEffectComponent>();
			var filter = world.Filter<EffectComponent>().End();

			#region IteratingEffects
			foreach (var entity in filter)
			{
				ref var fx = ref effectPool.Get(entity);
				fx.LifeTime -= Time.deltaTime;
			}
			#endregion

			#region HandlingDisposedEffects
			foreach (var entity in filter)
			{
				ref var fx = ref effectPool.Get(entity);

				if (fx.LifeTime <= 0)
				{
					fx.Effect.Hide();
					effectMainPool.Value.Add(fx.Effect);
					world.DelEntity(entity);
				}
			}
			#endregion

			#region CreatingEffects
			var requestFilter = world.Filter<RequestEffectComponent>().End();
			foreach (var entity in requestFilter)
			{
				ref var request = ref requetEffectsPool.Get(entity);

				var wrapper = effectsHolder.Value.GetEffect(request.EffectId);

				if (wrapper == null)
				{
					Debug.Log($"Couldn't find effect {request.EffectId} in EffectsHolder.");
					continue;
				}

				var effect = SpawnEffect(effectMainPool, wrapper);

				effect.transform.position = request.Position;

				if (effect != null)
				{
					effect.Show();
				}
				world.DelEntity(entity);
			}
			#endregion
		}

		/// <summary>
		/// Spawn new mob or take used mob from pool
		/// </summary>
		private SceneEffect SpawnEffect(EffectPoolComponent pool, FxWrapper config)
		{
			SceneEffect effect;
			if (pool.Value != null &&
				pool.Value.Count > 0 &&
				pool.Value.Any(b => b.Id.Equals(config.Id)))
			{
				effect = pool.Value.First(mob => mob.Id.Equals(config.Id));
				pool.Value.Remove(effect);
			}
			else
			{
				effect = Object.Instantiate(
					config.Prefab,
					pool.Parent);
				effect.Initialize(config.Id);
			}
			return effect;
		}
	}
}