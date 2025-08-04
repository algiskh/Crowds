using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class WeaponFireSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region GettingPoolsAndSingletons
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			#endregion


			var capacity = muzzle.GunConfig.MagazineCapacity;


			var isCoolDownPassed = muzzle.CoolDown <= 0;

			// Handle fire requests
			var fireFilter = world.Filter<RequestFireComponent>().End();
			var hasRequest = fireFilter.GetEntitiesCount() > 0;

			if (reloading.ReloadTime > 0)
			{
				Debug.Log($"Reloading... {reloading.ReloadTime}");
			}
			else
			if (hasRequest && muzzle.CurrentMagazineCount <= 0 && isCoolDownPassed)
			{
				// Try to start reloading 
				if (hasRequest && muzzle.CurrentMagazineCount == 0 && muzzle.AmmoCount > 0)
				{
					ref var requestReload = ref world.CreateSimpleEntity<RequestReloadComponent>();
				}
				muzzle.IsFiring = false;
			}
			else if (hasRequest && isCoolDownPassed)
			{
				ref var requestSpawnBullet = ref world.CreateSimpleEntity<RequestSpawnBulletComponent>();
				requestSpawnBullet.Direction = muzzle.Weapon.Muzzle.forward;
				requestSpawnBullet.Position = muzzle.Weapon.Muzzle.transform.position;
				requestSpawnBullet.GunConfig = muzzle.GunConfig;

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
				muzzle.CoolDown -= Time.deltaTime;
				if (muzzle.CoolDown < 0)
				{
					muzzle.CoolDown = 0;
				}
			}
			world.DeleteAllWith<RequestFireComponent>();
		}
	}
}