using Leopotam.EcsLite;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ECS
{
	public class CollisionSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			#region GettingPools
			var world = systems.GetWorld();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();
			var bulletOverlapPool = world.GetPool<BulletOverlapComponent>();
			var disposedPool = world.GetPool<DisposableComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var bulletPool = world.GetPool<BulletComponent>();
			var lootPool = world.GetPool<LootComponent>();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();

			var playerTransform = player.Value.transform;

			var playerPool = world.GetPool<PlayerComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var borderPool = world.GetPool<BorderComponent>();
			#endregion

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

						ref var bloodEffect = ref world.CreateSimpleEntity<RequestDecalComponent>();
						bloodEffect.Position = transform.position;
						bloodEffect.Id = "Blood";
						bloodEffect.Direction = move.Direction;

					}
				}
			}



			//#region CheckingMobsCollision
			//var applyDamagePool = world.GetPool<RequestDamageComponent>();
			//foreach (var bulletEntity in bulletsList)
			//{
			//	ref var bulletMove = ref movePool.Get(bulletEntity);
			//	ref var bulletCollision = ref colliderPool.Get(bulletEntity);

			//	foreach (var mobEntity in mobList)
			//	{
			//		ref var mobMove = ref movePool.Get(mobEntity);
			//		if (mobMove.Transform.position.DistanceTo(bulletMove.Transform.position)
			//			< bulletCollision.Radius
			//			&& bulletPool.Has(bulletEntity))
			//		{
			//			ref var bulletComponent = ref bulletPool.Get(bulletEntity);
			//			var applyDamageEntity = world.NewEntity();
			//			ref var applyDamage = ref applyDamagePool.Add(applyDamageEntity);
			//			applyDamage.Damage = bulletComponent.Damage;
			//			applyDamage.TargetEntity = mobEntity;
			//			bulletComponent.IsDisposed = true; // Mark bullet for disposal
			//		}
			//	}
			//}
			//#endregion

			//#region CheckingPlayerWithMobCollision
			//var playerFilter = world.Filter<PlayerComponent>().Inc<HealthComponent>().End();
			//foreach (var mob in mobList)
			//{
			//	ref var mobMove = ref movePool.Get(mob);
			//	var distance = mobMove.Transform.position.DistanceTo(player.Value.transform.position);

			//	if (distance < collisionPool.Get(mob).Radius)
			//	{
			//		foreach (var playerEntity in playerFilter)
			//		{
			//			ref var playerComponent = ref playerPool.Get(playerEntity);
			//			if (playerComponent.Value == player.Value)
			//			{
			//				ref var healthComponent = ref healthPool.Get(playerEntity);
			//				healthComponent.CurrentHealth -= healthComponent.MaxHealth;
			//			}
			//		}
			//	}
			//}
			//#endregion

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
						ref var muzzle = ref world.GetAsSingleton<MuzzleComponent>();
						muzzle.Count += loot.Count;
					}
					else if(loot.LootType is LootType.Health)
					{
						ref var healthComponent = ref healthPool.Get(player.Value.Entity);

						healthComponent.CurrentHealth += loot.Count;
						if (healthComponent.CurrentHealth > healthComponent.MaxHealth)
						{
							healthComponent.CurrentHealth = healthComponent.MaxHealth;
						}
					}
				}
			}
			#endregion

			//#region CheckingPlayerWithBorderCollision
			//var borderFilter = world.Filter<BorderComponent>().Inc<ColliderComponent>().End();
			//foreach (var borderEntity in borderFilter)
			//{
			//	ref var border = ref borderPool.Get(borderEntity);
			//	ref var borderCollision = ref colliderPool.Get(borderEntity);
			//	if (player.Value.transform.position.DistanceTo(border.Transform.position)
			//		<= borderCollision.Radius)
			//	{
			//		border.IsPlayerNearBy = true;
			//	}
			//	else
			//	{
			//		border.IsPlayerNearBy = false;
			//	}
			//}
			//#endregion
		}
	}
}