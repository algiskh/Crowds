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

			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();

			var meleeSpawnPool = world.GetPool<RequestMeleeComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var moveComponentPool = world.GetPool<MoveComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var breakablePool = world.GetPool<BreakableComponent>();

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


				// Attacker's melee strength (Rage bonus, MeleeStrengthModifier): 1 when there's none.
				float strength = modifierPool.Has(spawnRequest.SourceEntity)
					? modifierPool.Get(spawnRequest.SourceEntity).GetModifier<MeleeStrengthModifier>()
					: 1f;
				bool rage = !Mathf.Approximately(strength, 1f);
				float strikeDamage = spawnRequest.Config.Damage * strength;

				ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
				effectRequest.EffectId = rage && !string.IsNullOrEmpty(spawnRequest.Config.RageEffectId)
					? spawnRequest.Config.RageEffectId
					: spawnRequest.Config.Id;
				effectRequest.Position = spawnRequest.Position;
				effectRequest.Rotation = spawnRequest.Rotation;

				// Strike sound (configured in MeleeConfig, like weapon sounds). Positional, because
				// the attacker (mob/player) has no shared AudioSource; melee is rare — no overhead.
				var soundId = spawnRequest.Config.AttackSoundId;
				if (!string.IsNullOrEmpty(soundId))
				{
					var clip = soundHolder.Value.GetClip(soundId);
					if (clip != null)
						AudioSource.PlayClipAtPoint(clip, spawnRequest.Position);
				}

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
						damageRequestPool.Add(world.NewEntity()) = new RequestDamageComponent
						{
							Target = world.PackEntity(targetEntity),
							Damage = strikeDamage,
							HasSource = true,
							SourcePosition = spawnRequest.Position
						};

						// Decal only for mobs (damage source — melee).
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

				// Breakable environment: a breakable has no MoveComponent (doesn't participate in the loop above),
				// so check the radius against its transform and the allowed damage source on the object itself.
				var breakableFilter = world.Filter<BreakableComponent>().Inc<HealthComponent>().End();
				foreach (var breakableEntity in breakableFilter)
				{
					ref var breakable = ref breakablePool.Get(breakableEntity);
					if (breakable.Config == null || !breakable.Config.CanBeDamagedBy(BreakableDamageSources.Melee))
						continue;
					if (breakable.Value == null)
						continue;

					var distance = (breakable.Value.transform.position - spawnRequest.Position).magnitude;
					if (distance > spawnRequest.Config.Radius)
						continue;

					damageRequestPool.Add(world.NewEntity()) = new RequestDamageComponent
					{
						Target = world.PackEntity(breakableEntity),
						Damage = strikeDamage
					};
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

				if (distance <= meleeRequest.Config.Radius)
				{
					return true;
				}
			}
			return false;
		}
	}
}
