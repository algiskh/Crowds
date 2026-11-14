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
					// Владелец умер/переиспользован → связь рвём, эффект уйдёт в пул (Pool() отцепит
					// child-эффект от моба, иначе он «воскрес» бы вместе с переиспользованным мобом).
					if (fx.ModifierOwner.Unpack(world, out var ownerEntity) && modifiersPool.Has(ownerEntity))
					{
						ref var modifierOwner = ref modifiersPool.Get(ownerEntity);

						if (!fx.IsChild)
						{
							fx.Effect.transform.position = modifierOwner.Transform.position;
						}

						// DoT: эффект живёт, пока на владельце есть модификатор этого типа урона.
						// Прочие (DamageType.Unknown, напр. SpeedShotDebuff) — пока жив сам связанный модификатор.
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
		/// Берёт эффект по id из стека пула или инстанцирует новый.
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