using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class BulletSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region GettingPoolsAndSingletons
			var bulletPool = world.GetPool<BulletComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var disposePool = world.GetPool<DisposableComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var bulletRequestPool = world.GetPool<RequestSpawnBulletComponent>();

			#endregion

			var delta = Time.deltaTime;

			// Check disposed bullets and return them to the pool
			var disposedFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().Inc<DisposableComponent>().End();
			foreach (var bulletEntity in disposedFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				ref var isDisposed = ref disposePool.Get(bulletEntity);
				if (isDisposed.IsDisposed || bullet.LifeTime <= 0)
				{
					bullet.Bullet.gameObject.SetActive(false);
					bulletPoolPool.Value.Push(bullet.Bullet);
					world.DelEntity(bulletEntity); // delete entity
				}
				else
				{
					bullet.LifeTime -= delta;
				}
			}

			// Handle fire requests
			var bulletFilter = world.Filter<RequestSpawnBulletComponent>().End();
			foreach (var entity in bulletFilter)
			{
				var bulletRequest = bulletRequestPool.Get(entity);

				for (var i = 0; i < bulletRequest.GunConfig.ProjectilesNumber; i++)
				{
					SpawnBullet(world, ref bulletPoolPool, bulletPool, movePool, disposePool, modifierPool, bulletRequest);
				}
			}

			world.DeleteAllWith<RequestSpawnBulletComponent>();
		}

		private void SpawnBullet(
			EcsWorld world, 
			ref BulletPoolComponent bulletPoolPool, 
			EcsPool<BulletComponent> bulletPool, 
			EcsPool<MoveComponent> movePool,
			EcsPool<DisposableComponent> disposePool,
			EcsPool<ModifierOwnerComponent> modifierPool,
			RequestSpawnBulletComponent bulletRequest
			)
		{
			Bullet bullet;
			if (bulletPoolPool.Value != null &&
				bulletPoolPool.Value.Count > 0)
			{
				bullet = bulletPoolPool.Value.Pop();
			}
			else
			{
				bullet = Object.Instantiate(
					bulletRequest.GunConfig.BulletPrefab,
					bulletPoolPool.Parent);
			}

			bullet.transform.position = bulletRequest.Position;
			bullet.gameObject.SetActive(true);

			var bulletEntity = world.NewEntity();
			ref var bulletComponent = ref bulletPool.Add(bulletEntity);
			bulletComponent.Bullet = bullet;
			bulletComponent.Damage = bulletRequest.GunConfig.BulletDamage;
			bulletComponent.LifeTime = bulletRequest.GunConfig.BulletLifeTime;
			bulletComponent.CheckType = bulletRequest.GunConfig.BulletCheckType;
			bulletComponent.PiercedTargets = default;
			ref var moveComponent = ref movePool.Add(bulletEntity);
			ref var disposeComponent = ref disposePool.Add(bulletEntity);
			ref var modifierComponent = ref modifierPool.Add(bulletEntity);
			disposeComponent.IsDisposed = false;

			float accuracy = bulletRequest.GunConfig.Accuracy;
			float maxAngle = (1f - accuracy) * 90f;

			// Отклонение только в горизонтальной плоскости
			float deviationAngle = Random.Range(-maxAngle, maxAngle);
			Vector3 finalDirection = Quaternion.AngleAxis(deviationAngle, Vector3.up) * bulletRequest.Direction.normalized;

			moveComponent.Direction = finalDirection;
			moveComponent.Speed = bulletRequest.GunConfig.BulletSpeed;
			moveComponent.Transform = bullet.transform;
		}

	}
}