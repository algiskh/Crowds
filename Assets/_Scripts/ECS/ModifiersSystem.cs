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
			var filter = world.Filter<ModifierOwnerComponent>().End();
			Dictionary<ModifierOwnerComponent, Modifier> modifiersToRemove = new Dictionary<ModifierOwnerComponent, Modifier>();
			foreach (var entity in filter)
			{
				ref var modifierOwner = ref modifierPool.Get(entity);

				foreach (var modifier in modifierOwner.Modifiers)
				{
					modifier.Lifetime -= Time.deltaTime;

					if (modifier.Lifetime <= 0)
					{
						modifiersToRemove.Add(modifierOwner, modifier);
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
	}
}