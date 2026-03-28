using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	public class ModifiersSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();

			var tryApplyModifierPool = world.GetPool<TryApplyModifierComponent>();

			var tryApplyFilter = world.Filter<TryApplyModifierComponent>().End();

			foreach (var entity in tryApplyFilter)
			{
				ref var request = ref tryApplyModifierPool.Get(entity);
				if (request.TargetEntity == -1)
					continue;
				if (!modifierPool.Has(request.TargetEntity))
				{
					continue;
				}
				ref var modifierOwner = ref modifierPool.Get(request.TargetEntity);
				if (modifierOwner.Modifiers == null)
				{
					modifierOwner.Modifiers = new List<Modifier>();
				}
				modifierOwner.Modifiers.Add(request.Modifier);
				Debug.Log($"Applied modifier {request.Modifier.Id} to entity {request.TargetEntity}");
				world.DelEntity(entity);
			}

			var filter = world.Filter<ModifierOwnerComponent>().End();
			Dictionary<ModifierOwnerComponent, Modifier> modifiersToRemove = new Dictionary<ModifierOwnerComponent, Modifier>();

			foreach (var entity in filter)
			{
				var deltaTime = Time.deltaTime;
				ref var modifierOwner = ref modifierPool.Get(entity);

				if (modifierOwner.Modifiers == null || modifierOwner.Modifiers.Count == 0)
					continue;

				foreach (var modifier in modifierOwner.Modifiers)
				{
					modifier.Lifetime -= deltaTime;

					if (modifier.Lifetime <= 0)
					{
						modifiersToRemove.Add(modifierOwner, modifier);
					}
					if (modifier is IIteratableModifier iterable)
					{
						IterateModifier(world, iterable, entity, deltaTime);
					}
				}
			}

			if (modifiersToRemove.Count > 0)
			{
				foreach (var kvp in modifiersToRemove)
				{
					var owner = kvp.Key;
					var modifier = kvp.Value;
					owner.Modifiers.Remove(modifier);
					Debug.Log($"Modifier {modifier.Id} removed from entity {owner.Entity}");
				}
			}
		}


		private void IterateModifier(EcsWorld world, IIteratableModifier modifier, int targetEntity, float deltaTime)
		{
			Debug.Log($"Trying to iterate modifier on entity {targetEntity}");
			if (modifier.TryIterate(deltaTime, out var value))
			{
				Debug.Log($"Iterating modifier on entity {targetEntity} with value {value}");
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