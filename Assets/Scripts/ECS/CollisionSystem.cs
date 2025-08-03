using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.Windows;

namespace ECS
{
	public class CollisionSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			#region GettingPools
			var world = systems.GetWorld();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();
			var bulletOverlapPool = world.GetPool<BulletOverlapComponent>();
			var disposedPool = world.GetPool<DisposableComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var bulletPool = world.GetPool<BulletComponent>();
			var lootPool = world.GetPool<LootComponent>();
			var mobPool = world.GetPool<MobComponent>();
			
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			var playerTransform = player.Value.transform;
			var playerPos = playerTransform.position;

			var playerPool = world.GetPool<PlayerComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var borderPool = world.GetPool<BorderComponent>();
			#endregion

			// todo: refactor
			ref var failWindow = ref world.GetAsSingleton<FailWindowComponent>();

			if (failWindow.Value.gameObject.activeSelf)
			{
				return;
			}

			#region CreatingCollidersList
			var filter = world.Filter<ColliderComponent>().End();

			var bulletsList = new List<int>();
			var mobDict = new Dictionary<int, Collider>();
			foreach (var entity in filter)
			{
				ref var colliderComponent = ref colliderPool.Get(entity);

				if (colliderComponent.CollisionType == CollisionType.Bullet)
				{
					bulletsList.Add(entity);
				}
				else if (colliderComponent.CollisionType == CollisionType.Mob)
				{
					mobDict.Add(entity,colliderComponent.Value);
				}
			}
			#endregion

			var bulletFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().Inc<BulletOverlapComponent>().Inc<DisposableComponent>().End();

			foreach (var bulletEntity in bulletFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				var transform = bullet.Bullet.transform;
				ref var overlap = ref bulletOverlapPool.Get(bulletEntity);
				ref var disposed = ref disposedPool.Get(bulletEntity);
				foreach (var mobKvp in mobDict) {
					if (overlap.colliders.Any(b => b == mobKvp.Value))
					{
						ref var bulletComponent = ref bulletPool.Get(bulletEntity);
						disposed.IsDisposed = true;

						ref var damage = ref world.CreateSimpleEntity<RequestDamageComponent>();
						damage.TargetEntity = mobKvp.Key;
						damage.Damage = bulletComponent.Damage;

						ref var move = ref movePool.Get(bulletEntity);

						ref var bloodDecal = ref world.CreateSimpleEntity<RequestDecalComponent>();
						bloodDecal.Position = transform.position;
						bloodDecal.Id = "Blood";
						bloodDecal.Direction = move.Direction;


						ref var bloodEffect = ref world.CreateSimpleEntity<RequestEffectComponent>();
						bloodEffect.EffectId = "blood";
						bloodEffect.Position = transform.position;

					}
				}
			}

			#region CheckingPlayerWithMobCollision
			var mobFilter = world.Filter<MobComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var mob = ref mobPool.Get(mobEntity);
				var distance = mob.Value.transform.position.DistanceTo(playerPos);
				Debug.Log($"mob is in {distance}m");
				if (distance < mob.Config.HitRadius && mob.Cooldown <= 0)
				{
					Debug.Log($"Try to request damage for player");
					ref var requestDamage = ref world.CreateSimpleEntity<RequestDamageComponent>();
					requestDamage.TargetEntity = player.Value.Entity;
					requestDamage.Damage = mob.Config.Damage;

					mob.Cooldown = mob.Config.HitCooldown;

					//Request Effect
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = "playerHit";
					effectRequest.Position = playerPos;

					ref var bloodDecal = ref world.CreateSimpleEntity<RequestDecalComponent>();
					bloodDecal.Position = playerPos;
					bloodDecal.Id = "Blood";
					bloodDecal.Direction = playerTransform.rotation * Vector3.forward;

				}
				else if (mob.Cooldown > 0)
				{
					mob.Cooldown -= Time.deltaTime;
				}
			}
			#endregion

			#region CheckingPlayerWithLootCollision
			var lootFilter = world.Filter<LootComponent>().Inc<DisposableComponent>().End();
			foreach (var lootEntity in lootFilter)
			{
				ref var loot = ref lootPool.Get(lootEntity);
				ref var disposable = ref disposedPool.Get(lootEntity);
				if (playerTransform.position.DistanceTo(loot.Loot.transform.position)
					<= mainHolder.Value.LootRadius)
				{
					disposable.IsDisposed = true;
					if (loot.LootType is LootType.Ammo)
					{
						muzzle.AmmoCount += loot.Count;

						ref var requestAmmoViewUpdate = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
					}
					else if(loot.LootType is LootType.Weapon)
					{
						var newConfig = mainHolder.Value.GunConfigHolder.GetConfig(loot.Id);
						if (newConfig == null)
							continue;

						muzzle.GunConfig = newConfig;
						muzzle.CurrentMagazineCount = newConfig.MagazineCapacity;
						ref var requestWeaponViewUpdate = ref world.CreateSimpleEntity<UpdateWeaponViewRequestComponent>();
					}
					else if(loot.LootType is LootType.Health)
					{
						ref var healthComponent = ref healthPool.Get(player.Value.Entity);

						healthComponent.CurrentHealth += loot.Count;
						if (healthComponent.CurrentHealth > healthComponent.MaxHealth)
						{
							healthComponent.CurrentHealth = healthComponent.MaxHealth;
						}
						ref var requestUIHealthUpdate = ref world.CreateSimpleEntity<UpdateHealthViewRequestComponent>();
					}
				}
			}
			#endregion
		}
	}
}