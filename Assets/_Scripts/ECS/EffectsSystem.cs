using Leopotam.EcsLite;
using System.Collections.Generic;
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

				if (fx.LifeTime <= 0 && fx.HasModifierLink)
				{
					// The owner died/was recycled → break the link, the effect returns to the pool (Pool() detaches
					// the child effect from the mob, otherwise it would "resurrect" with the recycled mob).
					if (fx.ModifierOwner.Unpack(world, out var ownerEntity) && modifiersPool.Has(ownerEntity))
					{
						ref var modifierOwner = ref modifiersPool.Get(ownerEntity);

						if (!fx.IsChild)
						{
							fx.Effect.transform.position = modifierOwner.Transform.position;
						}

						// DoT: the effect lives while the owner has a modifier of this damage type.
						// Others (DamageType.Unknown, e.g. SpeedShotDebuff) — while the linked modifier itself lives.
						bool stillActive = fx.DamageType != DamageType.Unknown
							? modifierOwner.HasModifierWithDamageType(fx.DamageType)
							: modifierOwner.Modifiers != null && modifierOwner.Modifiers.Contains(fx.LinkedModifier);

						if (!stillActive)
							fx.HasModifierLink = false;
					}
					else
					{
						fx.HasModifierLink = false;
					}
				}
				fx.LifeTime -= world.GameDeltaTime();
			}
			#endregion

			#region HandlingDisposedEffects
			foreach (var entity in filter)
			{
				ref var fx = ref effectPool.Get(entity);

				if (fx.LifeTime <= 0 && !fx.HasModifierLink)
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

				// Fuse: defer the spawn until the delay elapses (staggered destruction bursts).
				if (request.Delay > 0f)
				{
					request.Delay -= world.GameDeltaTime();
					continue;
				}

				var wrapper = effectsHolder.Value.GetEffect(request.EffectId);

				if (wrapper == null)
				{
					Debug.Log($"Setting effect: Couldn't find effect {request.EffectId} in EffectsHolder.");
					continue;
				}

				var effect = SpawnEffect(effectMainPool, wrapper, request.Rotation);

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
						effectComponent.IsChild = wrapper.IsChild;
						if (wrapper.IsChild)
						{
							effectComponent.Effect.SetParent(request.Parent);
						}
						effectComponent.DamageType = request.DamageType;
						effectComponent.HasModifierLink = request.HasModifierLink;
						effectComponent.ModifierOwner = request.ModifierOwner;
						effectComponent.LinkedModifier = request.LinkedModifier;
					}
					else
					{
						effect.transform.position = request.Position;
					}
				}
				world.DelEntity(entity);
			}
			#endregion
		}

		/// <summary>
		/// Takes an effect by id from the pool stack or instantiates a new one.
		/// </summary>
		private SceneEffect SpawnEffect(EffectPoolComponent pool, FxWrapper config, float rotation)
		{
			SceneEffect effect;
			if (pool.Pools != null && pool.Pools.TryGetValue(config.Id, out var stack) && stack.Count > 0)
			{
				effect = stack.Pop();
			}
			else
			{
				effect = Object.Instantiate(config.Prefab, pool.Parent);
				effect.Initialize(config.Id);
			}
			effect.transform.eulerAngles = new Vector3(0, rotation, 0);
			return effect;
		}
	}
}
