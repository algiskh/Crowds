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
				// Цель могла умереть в DamageSystem прошлого кадра — запрос удаляем в любом случае.
				if (!request.Target.Unpack(world, out var target) || !modifierPool.Has(target) || request.Modifier == null)
				{
					world.DelEntity(entity);
					continue;
				}

				ref var modifierOwner = ref modifierPool.Get(target);
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
							HasModifierLink = true,
							ModifierOwner = request.Target,
							LinkedModifier = request.Modifier
						};
					}
				}
				world.DelEntity(entity);
			}

			_toRemove.Clear();
			var deltaTime = world.GameDeltaTime();

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
					// Отдельная сущность на каждый тик: несколько DoT на одной цели могут тикнуть в одном
					// кадре, а Add на саму цель второй раз бросил бы «already attached».
					ref var request = ref world.CreateSimpleEntity<RequestDamageComponent>();
					request.Target = world.PackEntity(targetEntity);
					request.Damage = value;
					request.IsPeriodic = true;
				}
			}
		}
	}
}