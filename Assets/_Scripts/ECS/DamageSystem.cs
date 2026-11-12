using Leopotam.EcsLite;
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
			var requestDamagePool = world.GetPool<RequestDamageComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var playerPool = world.GetPool<PlayerComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var crowdPool = world.GetPool<CrowdInstanceComponent>();
			List<int> entitiesWithLoot = new();

			#region Handling DamageRequests
			var filter = world.Filter<RequestDamageComponent>().End();
			foreach (var requestEntity in filter)
			{
				var requestDamageComponent = requestDamagePool.Get(requestEntity);

				// Цель могла умереть/переиспользоваться с момента создания запроса (запросы от систем,
				// идущих после DamageSystem, доживают до следующего кадра). Запрос удаляем всегда,
				// иначе он висел бы вечно и мог попасть в чужую сущность после рецикла id.
				if (!requestDamageComponent.Target.Unpack(world, out var target) || !healthPool.Has(target))
				{
					requestDamagePool.Del(requestEntity);
					continue;
				}

				var isPlayer = playerPool.Has(target);

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
							Target = world.PackEntity(target),
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
					float deathAnimTime = mobComponent.Config != null ? mobComponent.Config.DeathAnimationTime : 0f;

					// Skinned mob with a death animation: the entity dies now (below), the view lingers to play
					// "die" and is pooled later by DyingMobSystem. VAT mobs have no live Animator → instant.
					if (deathAnimTime > 0f && deadMob.Animator != null && !crowdPool.Has(mobEntity))
					{
						if (deadMob.Collider != null)
							deadMob.Collider.enabled = false;
						deadMob.ValueBar?.SetVisible(false);
						deadMob.Animator.SetAnimation(Scene.Animation.AnimationType.Die);

						ref var dying = ref world.GetPool<DyingMobComponent>().Add(world.NewEntity());
						dying.Value = deadMob;
						dying.Timer = deathAnimTime;
					}
					else
					{
						DyingMobSystem.ReturnToPool(world, deadMob);
					}



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
