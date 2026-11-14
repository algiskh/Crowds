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
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var bulletPoolPool = ref world.GetAsSingleton<BulletPoolComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			var soundHolder = world.GetAsSingleton<SoundHolderComponent>();
			var ammoInventory = world.GetAsSingleton<AmmoInventoryComponent>();
			#endregion

			var reserveAmmo = ammoInventory.Get(weapon.GunConfig.Caliber);

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
				if (hasRequest && weapon.CurrentMagazineCount == 0 && reserveAmmo > 0)
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
				requestSpawnBullet.Team = BulletTeam.Player;

				weapon.IsFiring = true;
				weapon.CoolDown = weapon.GunConfig.FireCoolDown;
				weapon.CurrentMagazineCount--;
				weapon.ShotCounter++;
				SpawnMuzzleFlash(world, weapon.Weapon.Muzzle);

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

		/// <summary>
		/// Muzzle flash on the gun the player visibly holds (PlayerAnimator's GunConfig.Model, its Muzzle and
		/// MuzzleEffectId); no body model → at the gameplay muzzle. Bullets still come from the gameplay muzzle.
		/// </summary>
		private static void SpawnMuzzleFlash(EcsWorld world, Transform gameplayMuzzle)
		{
			WeaponModel model = null;
			if (world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null && player.Value.BodyAnimator != null)
				model = player.Value.BodyAnimator.CurrentGunModel;

			var muzzle = model != null && model.Muzzle != null ? model.Muzzle : gameplayMuzzle;
			string effectId = model != null ? model.MuzzleEffectId : DefaultMuzzleEffectId;
			if (muzzle == null || string.IsNullOrEmpty(effectId))
				return;

			ref var fx = ref world.CreateSimpleEntity<RequestEffectComponent>();
			fx.EffectId = effectId;
			fx.Position = muzzle.position;
			fx.Parent = muzzle;
			fx.Rotation = Quaternion.LookRotation(muzzle.forward).eulerAngles.y;
		}

		// Same effect the mobs' WeaponModel uses by default (EffectsHolder id).
		private const string DefaultMuzzleEffectId = "muzzle_flash";

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