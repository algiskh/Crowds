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
			var modifiersPool = world.GetPool<ModifierOwnerComponent>();
			var moveComponentPool = world.GetPool<MoveComponent>();

			var filter = world.Filter<EffectComponent>().End();

			#region IteratingEffects
			foreach (var entity in filter)
			{
				ref var fx = ref effectPool.Get(entity);

				if (fx.LifeTime <= 0 && fx.ModifierEntity != 0)
				{
					if (fx.DamageType != DamageType.Unknown && modifiersPool.Has(fx.ModifierEntity))
					{
						ref var modifierOwner = ref modifiersPool.Get(fx.ModifierEntity);
						if (!modifierOwner.HasModifierWithDamageType(fx.DamageType))
						{
							fx.ModifierEntity = 0;
						}
					}
				}
				fx.LifeTime -= Time.deltaTime;
			}
			#endregion

			#region HandlingDisposedEffects
			foreach (var entity in filter)
			{
				ref var fx = ref effectPool.Get(entity);

				if (fx.LifeTime <= 0 && fx.ModifierEntity <= 0)
				{
					effectMainPool.Pool(fx.Effect);
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

				var effect = SpawnEffect(effectMainPool, wrapper, request.Rotation);

				Debug.Log($"Rotation is {request.Rotation}");

				effect.transform.position = request.Position;

				if (effect != null)
				{
					effect.Show();
					var newEntity = world.NewEntity();
					ref var effectComponent = ref effectPool.Add(newEntity);
					effectComponent.Effect = effect;
					effectComponent.LifeTime = wrapper.Duration;

					// Set modifier entity and damage type if they are specified in the request
					if (request.Parent != null)
					{
						effect.transform.position = request.Parent.position;
						effectComponent.Effect.SetParent(request.Parent);
						effectComponent.DamageType = request.DamageType;
					}
				}
				world.DelEntity(entity);
			}
			#endregion
		}

		/// <summary>
		/// Spawn new mob or take used mob from pool
		/// </summary>
		private SceneEffect SpawnEffect(EffectPoolComponent pool, FxWrapper config, float rotation)
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
			effect.transform.eulerAngles = new Vector3(0, rotation, 0);
			return effect;
		}
	}
}