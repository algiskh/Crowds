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
			var requestFirePool = world.GetPool<RequestFireComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var disposePool = world.GetPool<DisposableComponent>();
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			#endregion

			var capacity = muzzle.GunConfig.MagazineCapacity;

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
					bullet.LifeTime -= Time.deltaTime;
				}
			}

			var isCoolDownPassed = Time.time >= muzzle.PrevFireTime + muzzle.GunConfig.FireCoolDown;

			// Handle fire requests
			var fireFilter = world.Filter<RequestFireComponent>().End();
			var hasRequest = fireFilter.GetEntitiesCount() > 0;

			if (!hasRequest && isCoolDownPassed
				|| muzzle.CurrentMagazineCount <= 0
				|| reloading.ReloadTime > 0)
			{
				
				// Try to start reloading 
				if (hasRequest && muzzle.CurrentMagazineCount == 0 && muzzle.AmmoCount > 0 && reloading.ReloadTime <= 0)
				{
					var sound = soundHolder.Value.GetClip("reload");
					muzzle.Weapon.AudioSource.PlayOneShot(sound);
					reloading.ReloadTime = muzzle.GunConfig.ReloadTime;
				}

				muzzle.IsFiring = false;
			}
			else
			{
				foreach (var entity in fireFilter)
				{
					if (isCoolDownPassed)
					{
						ref var request = ref requestFirePool.Get(entity);
						Bullet bullet;
						if (bulletPoolPool.Value != null &&
							bulletPoolPool.Value.Count > 0)
						{
							bullet = bulletPoolPool.Value.Pop();
						}
						else
						{
							//Debug.Log($"muzzle.GunConfig.BulletPrefab is null {muzzle.GunConfig.BulletPrefab == null}");
							//Debug.Log($"muzzle.Weapon.Muzzle is null {muzzle.Weapon.Muzzle}");
							bullet = Object.Instantiate(
								muzzle.GunConfig.BulletPrefab,
								bulletPoolPool.Parent);
						}

						bullet.transform.position = muzzle.Weapon.Muzzle.transform.position;
						bullet.gameObject.SetActive(true);
						bullet.transform.position = muzzle.Weapon.Muzzle.transform.position;

						var bulletEntity = world.NewEntity();
						ref var bulletComponent = ref bulletPool.Add(bulletEntity);
						bulletComponent.Bullet = bullet;
						bulletComponent.Damage = muzzle.GunConfig.BulletDamage;
						bulletComponent.LifeTime = muzzle.GunConfig.BulletLifeTime;
						bulletComponent.CheckType = muzzle.GunConfig.BulletCheckType;
						ref var moveComponent = ref movePool.Add(bulletEntity);
						ref var disposeComponent = ref disposePool.Add(bulletEntity);
						disposeComponent.IsDisposed = false;

						moveComponent.Direction = muzzle.Weapon.Muzzle.forward;
						moveComponent.Speed = muzzle.GunConfig.BulletSpeed;
						moveComponent.Transform = bullet.transform;

						muzzle.IsFiring = true;
						muzzle.PrevFireTime = Time.time;
						muzzle.CurrentMagazineCount--;

						var sound = soundHolder.Value.GetClip(muzzle.GunConfig.FireSoundId);
						muzzle.Weapon.AudioSource.PlayOneShot(sound);
						ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
					}
					world.DelEntity(entity);
				}
			}

			// Finish reloading
			if (reloading.ReloadTime > 0)
			{
				reloading.ReloadTime -= Time.deltaTime;
				if (reloading.ReloadTime <= 0)
				{
					muzzle.CurrentMagazineCount = capacity >= muzzle.AmmoCount ? capacity : muzzle.AmmoCount;
					muzzle.AmmoCount -= muzzle.CurrentMagazineCount;
					reloading.ReloadTime = 0;
					// FIX HERE
				}
			}
		}
	}
}