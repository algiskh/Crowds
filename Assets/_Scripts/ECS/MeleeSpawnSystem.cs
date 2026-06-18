using Leopotam.EcsLite;
using System;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class MeleeSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var meleeSpawnPool = world.GetPool<RequestMeleeComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var moveComponentPool = world.GetPool<MoveComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var mobPool = world.GetPool<MobComponent>();

			var damageRequestPool = world.GetPool<RequestDamageComponent>();

			var filter = world.Filter<RequestMeleeComponent>().End();
			foreach (var entity in filter)
			{
				ref var spawnRequest = ref meleeSpawnPool.Get(entity);

				if (spawnRequest.Delay > 0)
				{
					spawnRequest.Delay -= Time.deltaTime;
					continue;
				}


				ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
				effectRequest.EffectId = spawnRequest.Config.Id;
				effectRequest.Position = spawnRequest.Position;
				effectRequest.Rotation = spawnRequest.Rotation;

				TryApplyDebuffs(modifierPool, spawnRequest);

				var healthFilter = world.Filter<HealthComponent>().End();
				foreach (var targetEntity in healthFilter)
				{
					ref var health = ref healthPool.Get(targetEntity);

					if (!spawnRequest.Config.TargetType.ContainsFlags(health.TargetType))
					{
						continue;
					}

					if (IsInRadius(targetEntity, moveComponentPool, spawnRequest))
					{
						Debug.Log($"Melee: DO damage to {targetEntity}");

						damageRequestPool.Add(world.NewEntity()) = new RequestDamageComponent
						{
							TargetEntity = targetEntity,
							Damage = spawnRequest.Config.Damage
						};

						// Декаль только для мобов (источник урона — ближний бой).
						if (mobPool.Has(targetEntity))
						{
							ref var mob = ref mobPool.Get(targetEntity);
							var mobPos = mob.Value.transform.position;
							world.RequestDamageDecal(mob.Config, DamageSourceType.Melee, mobPos, mobPos - spawnRequest.Position);
						}

						if (modifierPool.Has(targetEntity))
						{
							ref var modifier = ref modifierPool.Get(targetEntity);
							foreach (var meleeEffect in spawnRequest.Config.GetAllModifiersAsCopies())
							{
								modifier.Modifiers.Add(meleeEffect);
							}
						}
					}
				}
				meleeSpawnPool.Del(entity);
			}
		}

		private void TryApplyDebuffs(EcsPool<ModifierOwnerComponent> modifierPool, RequestMeleeComponent spawnRequest)
		{

			var debuffs = spawnRequest.Config.GetAllModifiersAsCopies(true);

			if (debuffs != null && debuffs.Count() > 0)
			{
				var hasEntity = modifierPool.Has(spawnRequest.SourceEntity);

				if (!hasEntity)
				{
					Debug.LogAssertion($"No modifier owner component on source entity {spawnRequest.SourceEntity}, but melee config has modifiers to apply! Adding component.");
					return;
				}

				var targetComp = modifierPool.Get(spawnRequest.SourceEntity);

				targetComp.Modifiers.AddRange(debuffs);
			}
		}

		private bool IsInRadius(int targetEntity, EcsPool<MoveComponent> moveComponentPool, RequestMeleeComponent meleeRequest)
		{
			if (moveComponentPool.Has(targetEntity))
			{
				ref var moveComponent = ref moveComponentPool.Get(targetEntity);

				var distance = (moveComponent.Transform.position - meleeRequest.Position).magnitude;

				Debug.Log($"Melee: Distance is {distance}. Radius is {meleeRequest.Config.Radius}");

				if (distance <= meleeRequest.Config.Radius)
				{
					return true;
				}
			}
			return false;
		}
	}
}