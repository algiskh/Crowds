using Leopotam.EcsLite;
using System.Collections.Generic;

namespace ECS
{
	public class ModifiersSystem : IEcsRunSystem
	{
		// Переиспользуемый scratch: (ownerEntity, modifier) пары на удаление.
		// Раньше был Dictionary<ModifierOwnerComponent, Modifier> — боксинг struct-ключа
		// и потенциальный конфликт/потеря при нескольких модификаторах у одного owner'а.
		private readonly List<(int ownerEntity, Modifier modifier)> _toRemove = new List<(int, Modifier)>(16);

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var effectsHolder = world.GetAsSingleton<EffectsHolderComponent>();
			var tryApplyModifierPool = world.GetPool<TryApplyModifierComponent>();
			var requestEffectPool = world.GetPool<RequestEffectComponent>();
			var tryApplyFilter = world.Filter<TryApplyModifierComponent>().End();

			foreach (var entity in tryApplyFilter)
			{
				ref var request = ref tryApplyModifierPool.Get(entity);
				if (request.TargetEntity == -1 || !modifierPool.Has(request.TargetEntity))
					continue;

				ref var modifierOwner = ref modifierPool.Get(request.TargetEntity);
				if (modifierOwner.Modifiers == null)
					modifierOwner.Modifiers = new List<Modifier>();

				modifierOwner.Modifiers.Add(request.Modifier);

				if (request.Modifier.HasEffect)
				{
					var effectConfig = effectsHolder.Value.GetEffect(request.Modifier.EffectId);
					if (effectConfig != null)
					{
						requestEffectPool.Add(world.NewEntity()) = new RequestEffectComponent
						{
							EffectId = request.Modifier.EffectId,
							Parent = modifierOwner.Transform,
							DamageType = request.Modifier is DamageModifier dmg ? dmg.Type : DamageType.Unknown,
							ModifierEntity = request.TargetEntity
						};
					}
				}
				world.DelEntity(entity);
			}

			_toRemove.Clear();
			var deltaTime = UnityEngine.Time.deltaTime;

			var filter = world.Filter<ModifierOwnerComponent>().End();
			foreach (var entity in filter)
			{
				ref var modifierOwner = ref modifierPool.Get(entity);
				var mods = modifierOwner.Modifiers;
				if (mods == null || mods.Count == 0)
					continue;

				for (int i = 0; i < mods.Count; i++)
				{
					var modifier = mods[i];
					modifier.Lifetime -= deltaTime;

					if (modifier.Lifetime <= 0)
						_toRemove.Add((entity, modifier));

					if (modifier is IIteratableModifier iterable)
						IterateModifier(world, iterable, entity, deltaTime);
				}
			}

			for (int i = 0; i < _toRemove.Count; i++)
			{
				var (ownerEntity, modifier) = _toRemove[i];
				if (!modifierPool.Has(ownerEntity))
					continue;
				ref var owner = ref modifierPool.Get(ownerEntity);
				owner.Modifiers?.Remove(modifier);
			}
		}

		private void IterateModifier(EcsWorld world, IIteratableModifier modifier, int targetEntity, float deltaTime)
		{
			if (modifier.TryIterate(deltaTime, out var value))
			{
				if (modifier is DamageModifier)
				{
					world.GetPool<RequestDamageComponent>().Add(targetEntity) = new RequestDamageComponent
					{
						TargetEntity = targetEntity,
						Damage = value
					};
				}
			}
		}
	}
}