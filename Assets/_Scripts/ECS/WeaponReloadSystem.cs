using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class WeaponReloadSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			#region Check pause  
			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
			{
				return;
			}
			#endregion

			#region GettingPoolsAndSingletons
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			#endregion

			var capacity = weapon.GunConfig.MagazineCapacity;
			// Handle fire requests
			var reloadFilter = world.Filter<RequestReloadComponent>().End();
			var isFiring = weapon.IsFiring;
			var hasFiringRequest = world.Filter<RequestFireComponent>().End().GetEntitiesCount() > 0;
			var hasReloadRequest = reloadFilter.GetEntitiesCount() > 0;

			if (weapon.GunConfig.SingleLoad && isFiring && hasReloadRequest && weapon.CurrentMagazineCount > 0 && reloading.ReloadTime == 0)
			{
				world.DeleteAllWith<RequestReloadComponent>();
				return;
			}

			if (reloading.ShutteringTime > 0)
			{
				reloading.ShutteringTime -= Time.deltaTime;
			}

			if (reloading.ReloadTime > 0)
			{
				reloading.ReloadTime -= Time.deltaTime;

				if (reloading.ReloadTime <= 0)
				{
					if (weapon.GunConfig.SingleLoad)
					{
						ReloadSingleAmmo(world, ref weapon, ref reloading, capacity , hasFiringRequest, soundHolder);
					}
					else
					{
						ReloadMagazine(world, ref weapon, ref reloading, capacity);
						StartShuttering(world, ref weapon, ref reloading, soundHolder);
					}
				}
				else
				{
					world.DeleteAllWith<RequestReloadComponent>();
				}
				ref var changeAmmoTextRequest = ref world.CreateSimpleEntity<UpdateAmmoViewRequestComponent>();
			}
			else
			if (hasReloadRequest && weapon.CurrentMagazineCount < capacity)
			{
				var sound = soundHolder.Value.GetClip(weapon.GunConfig.ReloadSoundId);
				if (sound == null)
				{
					sound = soundHolder.Value.GetClip("Reload");
				}
				weapon.Weapon.AudioSource.PlayOneShot(sound);
				reloading.ReloadTime = weapon.GunConfig.ReloadTime;
				world.DeleteAllWith<RequestReloadComponent>();
			}
		}

		private void ReloadSingleAmmo(EcsWorld world, ref WeaponComponent weapon, ref ReloadingComponent reloading, int capacity, bool hasFireRequest, SoundHolderComponent soundHolder)
		{
			if (weapon.CurrentMagazineCount < capacity && weapon.AmmoCount > 0)
			{
				weapon.CurrentMagazineCount++;
				weapon.AmmoCount--;
				reloading.ReloadTime = 0;
			}
			else
			{
				Debug.LogWarning("Cannot reload, magazine is full or no ammo left.");
				return;
			}

			if (weapon.CurrentMagazineCount >= capacity || weapon.AmmoCount <= 0 || hasFireRequest)
			{
				StartShuttering(world, ref weapon, ref reloading, soundHolder);
			}
			else
			if (weapon.CurrentMagazineCount < capacity && !hasFireRequest)
			{
				ref var additionalReloadRequest = ref world.CreateSimpleEntity<RequestReloadComponent>();
			}
		}

		private void ReloadMagazine(EcsWorld world, ref WeaponComponent weapon, ref ReloadingComponent reloading, int capacity)
		{
			var ammoNeeded = capacity - weapon.CurrentMagazineCount;

			var ammoToLoadCount = weapon.AmmoCount >= ammoNeeded ? ammoNeeded : weapon.AmmoCount;

			weapon.CurrentMagazineCount += ammoToLoadCount;
			weapon.AmmoCount -= ammoToLoadCount;
			reloading.ReloadTime = 0;
			world.DeleteAllWith<RequestReloadComponent>();
		}

		private void StartShuttering(EcsWorld world, ref WeaponComponent weapon, ref ReloadingComponent reloading, SoundHolderComponent soundHolder)
		{
			Debug.Log($"Start shuttering weapon {weapon.GunConfig.Id}");
			var sound = soundHolder.Value.GetClip(weapon.GunConfig.ReloadEndSoundId);
			if (sound != null)
			{
				weapon.Weapon.AudioSource.PlayOneShot(sound);
			}
			reloading.ShutteringTime = weapon.GunConfig.ShutterTime;
		}
	}
}