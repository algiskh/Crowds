using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class BulletSystem : IEcsRunSystem
	{
		// One pool per projectile prefab (BulletPoolComponent.ByPrefab): calibers have different meshes, so a bullet
		// returned by one gun must not be reused by another. Instances without SourcePrefab use the shared
		// BulletPoolComponent.Value stack (legacy). PoolPrewarmer fills these pools at load.

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
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;

			#endregion

			var delta = world.GameDeltaTime();

			// Check disposed bullets and return them to the pool
			var disposedFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().Inc<DisposableComponent>().End();
			foreach (var bulletEntity in disposedFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				ref var isDisposed = ref disposePool.Get(bulletEntity);
				if (isDisposed.IsDisposed || bullet.LifeTime <= 0)
				{
					bullet.Bullet.gameObject.SetActive(false);
					GetPool(ref bulletPoolPool, bullet.Bullet.SourcePrefab).Push(bullet.Bullet);
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
				var projectilePrefab = ResolveProjectile(mainHolder, bulletRequest.GunConfig.Caliber);

				for (var i = 0; i < bulletRequest.GunConfig.ProjectilesNumber; i++)
				{
					SpawnBullet(world, ref bulletPoolPool, bulletPool, movePool, disposePool, modifierPool, bulletRequest, projectilePrefab);
				}
			}

			world.DeleteAllWith<RequestSpawnBulletComponent>();
		}

		private Stack<Bullet> GetPool(ref BulletPoolComponent shared, Bullet prefab)
		{
			if (prefab == null)
				return shared.Value ??= new Stack<Bullet>();
			shared.ByPrefab ??= new Dictionary<Bullet, Stack<Bullet>>();
			if (!shared.ByPrefab.TryGetValue(prefab, out var pool))
				shared.ByPrefab[prefab] = pool = new Stack<Bullet>();
			return pool;
		}

		// Projectile prefab for the gun's caliber, falling back to the global bullet prefab.
		private Bullet ResolveProjectile(MainHolder mainHolder, Caliber caliber)
		{
			var ammoConfig = mainHolder.AmmoConfigHolder != null
				? mainHolder.AmmoConfigHolder.GetConfig(caliber)
				: null;
			return ammoConfig != null && ammoConfig.ProjectilePrefab != null
				? ammoConfig.ProjectilePrefab
				: mainHolder.BulletPrefab;
		}

		private void SpawnBullet(
			EcsWorld world,
			ref BulletPoolComponent bulletPoolPool,
			EcsPool<BulletComponent> bulletPool,
			EcsPool<MoveComponent> movePool,
			EcsPool<DisposableComponent> disposePool,
			EcsPool<ModifierOwnerComponent> modifierPool,
			RequestSpawnBulletComponent bulletRequest,
			Bullet projectilePrefab
			)
		{
			Bullet bullet;
			var pool = GetPool(ref bulletPoolPool, projectilePrefab);
			if (pool != null && pool.Count > 0)
			{
				bullet = pool.Pop();
			}
			else
			{
				bullet = Object.Instantiate(
					projectilePrefab,
					bulletPoolPool.Parent);
				bullet.SourcePrefab = projectilePrefab;
			}

			bullet.transform.position = bulletRequest.Position;
			// Pooled bullets are shared by all guns/teams: always (re)apply the gun's color or reset it.
			bullet.SetColor(bulletRequest.GunConfig.BulletColor);
			bullet.gameObject.SetActive(true);

			var bulletEntity = world.NewEntity();
			ref var bulletComponent = ref bulletPool.Add(bulletEntity);
			bulletComponent.Bullet = bullet;
			bulletComponent.Damage = bulletRequest.GunConfig.BulletDamage;
			bulletComponent.LifeTime = bulletRequest.GunConfig.BulletLifeTime;
			// Radius comes from the config: without it OverlapSphere/OverlapBox in BulletOverlapSystem
			// would work with a stale value's radius (EcsLite doesn't zero reusable structs).
			bulletComponent.Radius = bulletRequest.GunConfig.BulletRadius;
			bulletComponent.CheckType = bulletRequest.GunConfig.BulletCheckType;
			bulletComponent.Team = bulletRequest.Team;
			// On-shot debuffs (if set) — applied to the target via RequestDamage.DamageModifiers.
			bulletComponent.Modifiers = bulletRequest.GunConfig.ShotDebuffs;
			bulletComponent.PiercedTargets = default;
			bulletComponent.Gun = bulletRequest.GunConfig;
			// Per-gun pierce wins over the prefab: the bullet pool is shared between calibers,
			// so a pooled prefab's own MaxPierceCount isn't a reliable property of this shot.
			bulletComponent.MaxPierce = bulletRequest.GunConfig.PierceCount > 0
				? Mathf.Min(bulletRequest.GunConfig.PierceCount, GunConfig.MaxPierceCount)
				: bullet.MaxPierceCount;
			ref var moveComponent = ref movePool.Add(bulletEntity);
			ref var disposeComponent = ref disposePool.Add(bulletEntity);
			ref var modifierComponent = ref modifierPool.Add(bulletEntity);
			disposeComponent.IsDisposed = false;

			float accuracy = bulletRequest.GunConfig.Accuracy;
			float maxAngle = (1f - accuracy) * 90f;

			// Deviation only in the horizontal plane
			float deviationAngle = Random.Range(-maxAngle, maxAngle);
			Vector3 finalDirection = Quaternion.AngleAxis(deviationAngle, Vector3.up) * bulletRequest.Direction.normalized;

			// Nose (+Z) along the flight; the trail is emitted behind it.
			if (finalDirection.sqrMagnitude > 1e-6f)
				bullet.transform.rotation = Quaternion.LookRotation(finalDirection);
			if (bullet.TryGetComponent<TrailRenderer>(out var trail))
				trail.Clear();

			moveComponent.Direction = finalDirection;
			moveComponent.Speed = bulletRequest.GunConfig.BulletSpeed;
			moveComponent.Transform = bullet.transform;
		}

	}
}
