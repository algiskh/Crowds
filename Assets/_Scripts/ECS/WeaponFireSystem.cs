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

			// Spin-up (e.g. machine gun): the trigger must be held SpinUpTime before shots go out.
			weapon.SpinUp = hasRequest ? Mathf.Min(weapon.SpinUp + Time.deltaTime, 60f) : 0f;
			var isSpunUp = weapon.SpinUp >= weapon.GunConfig.SpinUpTime;

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
			else if (hasRequest && isCoolDownPassed && isSpunUp)
			{
				ref var requestSpawnBullet = ref world.CreateSimpleEntity<RequestSpawnBulletComponent>();
				var heldModel = GetHeldGunModel(world);
				requestSpawnBullet.Direction = weapon.Weapon.Muzzle.forward;
				requestSpawnBullet.Position = GetBulletOrigin(weapon.Weapon.Muzzle, heldModel);
				requestSpawnBullet.GunConfig = weapon.GunConfig;
				requestSpawnBullet.Team = BulletTeam.Player;

				weapon.IsFiring = true;
				weapon.CoolDown = weapon.GunConfig.FireCoolDown;
				weapon.CurrentMagazineCount--;
				weapon.ShotCounter++;
				SpawnMuzzleFlash(world, weapon.Weapon.Muzzle, heldModel);

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

		// Max rise of the bullet above the gameplay muzzle: guards against odd poses (barrel pointing up mid-clip).
		private const float MaxMuzzleRise = 1f;

		/// <summary>The gun model the player visibly holds (PlayerAnimator's GunConfig.Model), or null.</summary>
		private static WeaponModel GetHeldGunModel(EcsWorld world)
		{
			if (world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null && player.Value.BodyAnimator != null)
				return player.Value.BodyAnimator.CurrentGunModel;
			return null;
		}

		/// <summary>
		/// Bullet spawn point: the gameplay muzzle on the player root (XZ, so the aim line/crosshair stay exact) lifted
		/// to the height of the visible gun's muzzle — the animated body holds the gun higher than the root muzzle,
		/// so without this the bullets come out under the barrel. No body model → the gameplay muzzle as is.
		/// </summary>
		private static Vector3 GetBulletOrigin(Transform gameplayMuzzle, WeaponModel model)
		{
			var origin = gameplayMuzzle.position;
			if (model != null && model.Muzzle != null)
				origin.y = Mathf.Clamp(model.Muzzle.position.y, origin.y, origin.y + MaxMuzzleRise);
			return origin;
		}

		/// <summary>
		/// Muzzle flash on the gun the player visibly holds (its Muzzle and MuzzleEffectId); no body model → at the
		/// gameplay muzzle.
		/// </summary>
		private static void SpawnMuzzleFlash(EcsWorld world, Transform gameplayMuzzle, WeaponModel model)
		{
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