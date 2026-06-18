using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;

namespace ECS
{
	public class CollisionSystem : IEcsRunSystem
	{
		// Переиспользуется между кадрами. Локальная для системы — не singleton-компонент.
		private readonly List<Modifier> _modifierScratch = new List<Modifier>(8);

		public void Run(IEcsSystems systems)
		{
			#region GettingPools
			var world = systems.GetWorld();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();
			var bulletOverlapPool = world.GetPool<BulletOverlapComponent>();
			var disposedPool = world.GetPool<DisposableComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var bulletPool = world.GetPool<BulletComponent>();
			var lootPool = world.GetPool<LootComponent>();
			var mobPool = world.GetPool<MobComponent>();

			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			var playerTransform = player.Value.transform;
			var playerPos = playerTransform.position;

			var healthPool = world.GetPool<HealthComponent>();
			#endregion

			#region Check pause
			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;
			#endregion

			#region BulletVsMob
			var bulletFilter = world.Filter<BulletComponent>()
				.Inc<MoveComponent>()
				.Inc<ModifierOwnerComponent>()
				.Inc<BulletOverlapComponent>()
				.Inc<DisposableComponent>()
				.End();

			foreach (var bulletEntity in bulletFilter)
			{
				ref var bulletComponent = ref bulletPool.Get(bulletEntity);
				ref var overlap = ref bulletOverlapPool.Get(bulletEntity);
				ref var disposed = ref disposedPool.Get(bulletEntity);

				var bulletTransform = bulletComponent.Bullet.transform;
				int hitsLen = overlap.MobHits.Length;

				for (int i = 0; i < hitsLen; i++)
				{
					int mobEntity = overlap.MobHits[i];

					// Entity мог быть удалён (умер/ушёл в пул) между кадрами.
					if (!mobPool.Has(mobEntity))
						continue;

					int maxPierce = bulletComponent.Bullet.MaxPierceCount;
					if (maxPierce > 1 && bulletComponent.PiercedTargets.Length < maxPierce - 1)
					{
						if (bulletComponent.PiercedTargets.ContainsFixed(mobEntity))
							continue;
						bulletComponent.PiercedTargets.Add(mobEntity);
					}
					else
					{
						disposed.IsDisposed = true;
					}

					ref var damage = ref world.CreateSimpleEntity<RequestDamageComponent>();
					damage.TargetEntity = mobEntity;
					damage.Damage = bulletComponent.Damage;

					ref var move = ref movePool.Get(bulletEntity);
					var hitMobConfig = mobPool.Get(mobEntity).Config;

					world.RequestDamageDecal(hitMobConfig, DamageSourceType.Bullet, bulletTransform.position, move.Direction);

					ref var bloodEffect = ref world.CreateSimpleEntity<RequestEffectComponent>();
					bloodEffect.EffectId = "blood";
					bloodEffect.Position = bulletTransform.position;

					if (disposed.IsDisposed) break;
				}
			}
			#endregion

			#region PlayerVsMob
			var mobFilter = world.Filter<MobComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var mob = ref mobPool.Get(mobEntity);
				var distance = mob.Value.transform.position.DistanceTo(playerPos);
				if (distance < mob.Config.HitRadius && mob.Cooldown <= 0)
				{
					ref var requestDamage = ref world.CreateSimpleEntity<RequestDamageComponent>();
					requestDamage.TargetEntity = player.Value.Entity;
					requestDamage.Damage = mob.Config.Damage;

					var attackMods = mob.Config.AttackModifiers;
					if (attackMods != null && attackMods.Length > 0)
					{
						_modifierScratch.Clear();
						for (int i = 0; i < attackMods.Length; i++)
						{
							var modifier = attackMods[i];
							if (modifier is DamageModifier dmgMod)
							{
								if (Random.value > dmgMod.Chance)
									continue;
							}
							_modifierScratch.Add(modifier.Clone<Modifier>());
						}
						if (_modifierScratch.Count > 0)
						{
							// Копируем в новый список: RequestDamage живёт дольше scratch'а.
							requestDamage.DamageModifiers = new List<Modifier>(_modifierScratch);
						}
					}

					mob.Cooldown = mob.Config.HitCooldown;

					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = "playerHit";
					effectRequest.Position = playerPos;

					ref var bloodDecal = ref world.CreateSimpleEntity<RequestDecalComponent>();
					bloodDecal.Position = playerPos;
					bloodDecal.Id = "Blood";
					bloodDecal.Direction = playerTransform.forward;
				}
				else if (mob.Cooldown > 0)
				{
					mob.Cooldown -= Time.deltaTime;
				}
			}
			#endregion

			#region PlayerVsLoot
			var lootFilter = world.Filter<LootComponent>().Inc<DisposableComponent>().End();
			float lootRadiusSqr = mainHolder.Value.LootRadius * mainHolder.Value.LootRadius;
			foreach (var lootEntity in lootFilter)
			{
				ref var loot = ref lootPool.Get(lootEntity);
				ref var disposable = ref disposedPool.Get(lootEntity);
				if (disposable.IsDisposed) continue;

				if ((playerTransform.position - loot.Loot.transform.position).sqrMagnitude <= lootRadiusSqr)
				{
					disposable.IsDisposed = true;
					switch (loot.LootType)
					{
						case LootType.Ammo:
							muzzle.AmmoCount += loot.Count;
							world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
							break;
						case LootType.Weapon:
							var newConfig = mainHolder.Value.GunConfigHolder.GetConfig(loot.Id);
							if (newConfig == null) continue;
							muzzle.GunConfig = newConfig;
							muzzle.CurrentMagazineCount = newConfig.MagazineCapacity;
							world.CreateSimpleEntity<UpdateWeaponViewRequestComponent>();
							break;
						case LootType.Health:
							ref var healthComponent = ref healthPool.Get(player.Value.Entity);
							healthComponent.CurrentHealth += loot.Count;
							if (healthComponent.CurrentHealth > healthComponent.MaxHealth)
								healthComponent.CurrentHealth = healthComponent.MaxHealth;
							world.CreateSimpleEntity<UpdateHealthViewRequestComponent>();
							break;
						case LootType.Grenade:
							ref var grenadeState = ref world.GetAsSingleton<GrenadeStateComponent>();
							var grenadeHolder = mainHolder.Value.GrenadeConfigHolder;
							if (grenadeHolder != null)
							{
								var pickedConfig = string.IsNullOrEmpty(loot.Id)
									? grenadeHolder.Default
									: grenadeHolder.GetConfig(loot.Id);
								if (pickedConfig != null)
									grenadeState.CurrentConfig = pickedConfig;
							}
							grenadeState.Count += loot.Count;
							world.CreateSimpleEntity<UpdateGrenadeViewRequestComponent>();
							break;
						case LootType.Bonus:
							if (mainHolder.Value.BonusConfigHolder != null)
							{
								ref var bonusRequest = ref world.CreateSimpleEntity<RequestApplyBonusComponent>();
								bonusRequest.ConfigId = loot.Id;
							}
							break;
					}
				}
			}
			#endregion
		}
	}
}