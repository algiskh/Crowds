using Leopotam.EcsLite;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
namespace ECS
{
	public class DamageSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var fragCount = ref world.GetAsSingleton<FragCountComponent>();
			ref var mobSpawnPool = ref world.GetAsSingleton<MobPoolComponent>(); 
			var requestDamagePool = world.GetPool<RequestDamageComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var playerPool = world.GetPool<PlayerComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			List<int> entitiesWithLoot = new();

			#region Handling DamageRequests
			var filter = world.Filter<RequestDamageComponent>().End();
			foreach (var requestEntity in filter)
			{
				var requestDamageComponent = requestDamagePool.Get(requestEntity);
				var target = requestDamageComponent.TargetEntity;

				var isPlayer = playerPool.Has(target);

				if (!healthPool.Has(target))
				{
					// If the target does not have health or mob component, skip processing
					continue;
				}

				ref var healthComponent = ref healthPool.Get(target);

				// Активный щит-бонус режет входящий урон: ShieldModifier.Value — множитель урона (0.5 = −50%).
				var damage = requestDamageComponent.Damage;
				if (modifierPool.Has(target))
					damage *= modifierPool.Get(target).GetModifier<ShieldModifier>();

				// Apply damage to health
				healthComponent.CurrentHealth -= damage;

				if (isPlayer)
				{
					ref var requestUIHealthUpdate = ref world.CreateSimpleEntity<UpdateHealthViewRequestComponent>();
					if (healthComponent.CurrentHealth <= 0)
					{
						Debug.Log($"END_GAME");
						ref var failedRequest = ref world.CreateSimpleEntity<EndGameComponent>();
					}
				}
				else if (mobPool.Has(target))
				{
					ref var mob = ref mobPool.Get(target);
					mob.Value.ValueBar
						.ApplyValue(healthComponent.CurrentHealth);

					//Request for loot
					if (healthComponent.CurrentHealth <= 0 && !entitiesWithLoot.Contains(target))
					{
						entitiesWithLoot.Add(target);
						ref var mobLoot = ref world.CreateSimpleEntity<RequestLootSpawn>();
						++fragCount.Value;
						ref var uiRequest = ref world.CreateSimpleEntity<RequestUpdateFragCountComponent>();
						mobLoot.SourceEntity = target;
						mobLoot.Source = RequestSpawnSource.Mob;
						mobLoot.PossibleLoots = mob.Config.PossibleLoots;
						mobLoot.Position = mob.Value.transform.position;
					}

				}

				if (requestDamageComponent.DamageModifiers != null)
				{
					var tryapplyModifierPool = world.GetPool<TryApplyModifierComponent>();
					foreach (var modifier in requestDamageComponent.DamageModifiers)
					{
						tryapplyModifierPool.Add(world.NewEntity()) = new TryApplyModifierComponent
						{
							TargetEntity = target,
							Modifier = modifier
						};
					}
				}

				requestDamagePool.Del(requestEntity);
			}
			#endregion

			#region Handling zombie health
			var mobFilter = world.Filter<MobComponent>().Inc<HealthComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				if (!mobPool.Has(mobEntity) || !healthPool.Has(mobEntity))
					continue;
				ref var mobComponent = ref mobPool.Get(mobEntity);
				ref var healthComponent = ref healthPool.Get(mobEntity);

				var position = mobComponent.Value.transform.position;

				if (healthComponent.CurrentHealth <= 0)
				{
					var deadMob = mobComponent.Value;
					if (mobSpawnPool.Pools == null)
						mobSpawnPool.Pools = new Dictionary<string, Stack<Mob>>();
					if (!mobSpawnPool.Pools.TryGetValue(deadMob.Id, out var stack))
					{
						stack = new Stack<Mob>();
						mobSpawnPool.Pools[deadMob.Id] = stack;
					}
					stack.Push(deadMob);
					deadMob.gameObject.SetActive(false);



					//Request Effect (per-mob-type death effect, fallback to the shared "zombie_dead")
					var deathEffectId = mobComponent.Config != null ? mobComponent.Config.DeathEffectId : null;
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = string.IsNullOrEmpty(deathEffectId) ? "zombie_dead" : deathEffectId;
					effectRequest.Position = position;

					world.DelEntity(mobEntity);
				}
			}
			#endregion
		}
	}
}
