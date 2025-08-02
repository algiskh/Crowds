using Leopotam.EcsLite;
using Unity.VisualScripting;
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
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			#endregion

			var capacity = muzzle.GunConfig.MagazineCapacity;

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

			var isCoolDownPassed = muzzle.CoolDown <= 0;

			// Handle fire requests
			var fireFilter = world.Filter<RequestFireComponent>().End();
			var hasRequest = fireFilter.GetEntitiesCount() > 0;

			if (reloading.ReloadTime > 0)
			{
				reloading.ReloadTime -= delta;
				if (reloading.ReloadTime <= 0)
				{
					muzzle.CurrentMagazineCount = muzzle.AmmoCount >= capacity ? capacity : muzzle.AmmoCount;
					muzzle.AmmoCount -= muzzle.CurrentMagazineCount;
					reloading.ReloadTime = 0;
				}
				Debug.Log($"Reloading... {reloading.ReloadTime}");
				ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
			}
			else
			if (hasRequest && muzzle.CurrentMagazineCount <= 0 && isCoolDownPassed)
			{
				// Try to start reloading 
				if (hasRequest && muzzle.CurrentMagazineCount == 0 && muzzle.AmmoCount > 0)
				{
					var sound = soundHolder.Value.GetClip("Reload");
					muzzle.Weapon.AudioSource.PlayOneShot(sound);
					Debug.Log("PlaySound ^ Reload");
					reloading.ReloadTime = muzzle.GunConfig.ReloadTime;
				}
				muzzle.IsFiring = false;
			}
			else if (hasRequest && isCoolDownPassed)
			{
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
				muzzle.CoolDown = muzzle.GunConfig.FireCoolDown;
				muzzle.CurrentMagazineCount--;
				Debug.Log($"Fired bullet, Current Magazine Count: {muzzle.CurrentMagazineCount}. Set cooldown {muzzle.CoolDown}");

				var sound = soundHolder.Value.GetClip(muzzle.GunConfig.FireSoundId);
				muzzle.Weapon.AudioSource.PlayOneShot(sound);
				ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
			}
			else if (!hasRequest && isCoolDownPassed)
			{
				muzzle.IsFiring = false;
			}


			if (muzzle.CoolDown > 0)
			{
				muzzle.CoolDown -= delta;
				if (muzzle.CoolDown < 0)
				{
					muzzle.CoolDown = 0;
				}
			}
			world.DeleteAllWith<RequestFireComponent>();
		}

	}
}