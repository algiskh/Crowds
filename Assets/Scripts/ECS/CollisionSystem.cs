using Leopotam.EcsLite;
using System.Collections.Generic;

namespace ECS
{
	public class CollisionSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			#region GettingPools
			var world = systems.GetWorld();
			var collisionPool = world.GetPool<CollisionComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var bulletPool = world.GetPool<BulletComponent>();
			var lootPool = world.GetPool<LootComponent>();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			var playerPool = world.GetPool<PlayerComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var borderPool = world.GetPool<BorderComponent>();
			#endregion

			#region CreatingCollidersList
			var filter = world.Filter<CollisionComponent>().Inc<MoveComponent>().End();

			var bulletsList = new List<int>();
			var mobList = new List<int>();
			foreach (var entity in filter)
			{
				ref var collisionComponent = ref collisionPool.Get(entity);
				ref var moveComponent = ref movePool.Get(entity);

				if (collisionComponent.CollisionType == CollisionType.Bullet)
				{
					bulletsList.Add(entity);
				}
				else if (collisionComponent.CollisionType == CollisionType.Mob)
				{
					mobList.Add(entity);
				}
			}
			#endregion

			#region CheckingMobsCollision
			var applyDamagePool = world.GetPool<RequestDamageComponent>();
			foreach (var bulletEntity in bulletsList)
			{
				ref var bulletMove = ref movePool.Get(bulletEntity);
				ref var bulletCollision = ref collisionPool.Get(bulletEntity);

				foreach (var mobEntity in mobList)
				{
					ref var mobMove = ref movePool.Get(mobEntity);
					if (mobMove.Transform.position.DistanceTo(bulletMove.Transform.position)
						< bulletCollision.Radius
						&& bulletPool.Has(bulletEntity))
					{
						ref var bulletComponent = ref bulletPool.Get(bulletEntity);
						var applyDamageEntity = world.NewEntity();
						ref var applyDamage = ref applyDamagePool.Add(applyDamageEntity);
						applyDamage.Damage = bulletComponent.Damage;
						applyDamage.TargetEntity = mobEntity;
						bulletComponent.IsDisposed = true; // Mark bullet for disposal
					}
				}
			}
			#endregion

			#region CheckingPlayerWithMobCollision
			var playerFilter = world.Filter<PlayerComponent>().Inc<HealthComponent>().End();
			foreach (var mob in mobList)
			{
				ref var mobMove = ref movePool.Get(mob);
				var distance = mobMove.Transform.position.DistanceTo(player.Value.transform.position);

				if (distance < collisionPool.Get(mob).Radius)
				{
					foreach (var playerEntity in playerFilter)
					{
						ref var playerComponent = ref playerPool.Get(playerEntity);
						if (playerComponent.Value == player.Value)
						{
							ref var healthComponent = ref healthPool.Get(playerEntity);
							healthComponent.CurrentHealth -= healthComponent.MaxHealth;
						}
					}
				}
			}
			#endregion

			#region CheckingPlayerWithLootCollision
			var lootFilter = world.Filter<LootComponent>().Inc<CollisionComponent>().End();
			foreach (var lootEntity in lootFilter)
			{
				ref var loot = ref lootPool.Get(lootEntity);
				ref var lootCollision = ref collisionPool.Get(lootEntity);

				if (player.Value.transform.position.DistanceTo(loot.Loot.transform.position)
					<= lootCollision.Radius)
				{
					loot.IsDisposed = true;
					if (loot.LootType is LootType.Ammo)
					{
						ref var muzzle = ref world.GetAsSingleton<MuzzleComponent>();
						muzzle.Count += loot.Count;
					}
				}
			}
			#endregion

			#region CheckingPlayerWithBorderCollision
			var borderFilter = world.Filter<BorderComponent>().Inc<CollisionComponent>().End();
			foreach (var borderEntity in borderFilter)
			{
				ref var border = ref borderPool.Get(borderEntity);
				ref var borderCollision = ref collisionPool.Get(borderEntity);
				if (player.Value.transform.position.DistanceTo(border.Transform.position)
					<= borderCollision.Radius)
				{
					border.IsPlayerNearBy = true;
				}
				else
				{
					border.IsPlayerNearBy = false;
				}
			}
			#endregion
		}
	}
}