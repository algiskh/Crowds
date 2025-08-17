using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class WeaponReloadSystem : IEcsRunSystem
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
			// Handle fire requests
			var reloadFilter = world.Filter<RequestReloadComponent>().End();
			var hasRequest = reloadFilter.GetEntitiesCount() > 0;

		//TODO: Refactor
			if (reloading.ReloadTime > 0)
			{
				reloading.ReloadTime -= Time.deltaTime;
				if (reloading.ReloadTime <= 0 )
				{
					var ammountBeforeReload = muzzle.CurrentMagazineCount;
					var ammoToLoadCount = muzzle.AmmoCount >= capacity ? capacity : muzzle.AmmoCount;

					muzzle.CurrentMagazineCount = ammoToLoadCount;
					muzzle.AmmoCount -= ammoToLoadCount - ammountBeforeReload;
					reloading.ReloadTime = 0;
				}
				ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
			}
			else
			if (hasRequest && muzzle.CurrentMagazineCount < capacity)
			{
				var sound = soundHolder.Value.GetClip("Reload");
				muzzle.Weapon.AudioSource.PlayOneShot(sound);
				reloading.ReloadTime = muzzle.GunConfig.ReloadTime;
			}
			world.DeleteAllWith<RequestReloadComponent>();
		}
	}
}