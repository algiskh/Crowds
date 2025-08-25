using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.LightTransport;

namespace ECS
{
	public class WeaponFireSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region GettingPoolsAndSingletons
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			#endregion

			var isCoolDownPassed = weapon.CoolDown <= 0;

			// Handle fire requests
			var fireFilter = world.Filter<RequestFireComponent>().End();
			var hasRequest = fireFilter.GetEntitiesCount() > 0;

			if (reloading.ReloadTime > 0 || reloading.ShutteringTime > 0)
			{
				if (weapon.GunConfig.SingleLoad && weapon.CurrentMagazineCount > 0)
				{
					IterateCooldown(ref weapon);
					return;
				}
			}
			else
			if (hasRequest && weapon.CurrentMagazineCount <= 0 && isCoolDownPassed)
			{
				// Try to start reloading 
				if (hasRequest && weapon.CurrentMagazineCount == 0 && weapon.AmmoCount > 0)
				{
					ref var requestReload = ref world.CreateSimpleEntity<RequestReloadComponent>();
				}
				weapon.IsFiring = false;
			}
			else if (hasRequest && isCoolDownPassed)
			{
				ref var requestSpawnBullet = ref world.CreateSimpleEntity<RequestSpawnBulletComponent>();
				requestSpawnBullet.Direction = weapon.Weapon.Muzzle.forward;
				requestSpawnBullet.Position = weapon.Weapon.Muzzle.transform.position;
				requestSpawnBullet.GunConfig = weapon.GunConfig;

				weapon.IsFiring = true;
				weapon.CoolDown = weapon.GunConfig.FireCoolDown;
				weapon.CurrentMagazineCount--;
				Debug.Log($"Fired bullet, Current Magazine Count: {weapon.CurrentMagazineCount}. Set cooldown {weapon.CoolDown}");

				var sound = soundHolder.Value.GetClip(weapon.GunConfig.FireSoundId);
				weapon.Weapon.AudioSource.PlayOneShot(sound);
				ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
			}
			else if (!hasRequest && isCoolDownPassed)
			{
				weapon.IsFiring = false;
			}


			IterateCooldown(ref weapon);
			world.DeleteAllWith<RequestFireComponent>();
		}

		private void IterateCooldown(ref WeaponComponent muzzle)
		{
			if (muzzle.CoolDown > 0)
			{
				muzzle.CoolDown -= Time.deltaTime;
				if (muzzle.CoolDown < 0)
				{
					muzzle.CoolDown = 0;
				}
			}
		}
	}
}