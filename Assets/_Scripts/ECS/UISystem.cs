using Leopotam.EcsLite;
using System.Diagnostics;

namespace ECS
{
	public class UISystem : IEcsRunSystem
	{
		// Whether the reload cycle was active last frame — to top the icon up to 1 when it finishes.
		private bool _wasReloading;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var fragCount = ref world.GetAsSingleton<FragCountComponent>();
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var weaponView = ref world.GetAsSingleton<WeaponUIViewComponent>();
			ref var playerStats = ref world.GetAsSingleton<PlayerStatsComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			ref var difficultyView = ref world.GetAsSingleton<DifficultyTimerUIComponent>();
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();
			var ammoInventory = world.GetAsSingleton<AmmoInventoryComponent>();

			var healthPool = world.GetPool<HealthComponent>();
			var requestPool = world.GetPool<RequestOpenWindowComponent>();
			var filter = world.Filter<RequestOpenWindowComponent>()
				.End();
			foreach (var requestEntity in filter)
			{
				ref var request = ref requestPool.Get(requestEntity);
				if (request.WindowType is WindowType.FailWindow)
				{
					ref var failWindow = ref world.GetAsSingleton<FailWindowComponent>();
					failWindow.Value.Show(fragCount.Value);
				}else if (request.WindowType is WindowType.WinWindow)
				{
					ref var winWindow = ref world.GetAsSingleton<WinWindowComponent>();
					winWindow.Value.Show(fragCount.Value);
				}
				world.DelEntity(requestEntity);
			}

			if (reloading.ReloadTime > 0 || reloading.ShutteringTime > 0)
			{
				weaponView.Value.ShowReloading(GetReloadProgress(reloading, weapon.GunConfig));
				_wasReloading = true;
			}
			else if (_wasReloading)
			{
				weaponView.Value.ShowReloading(1f);
				_wasReloading = false;
			}

			var ammoRequestFilter = world.Filter<UpdateAmmoViewRequestComponent>()
				.End();

			var weaponRequestFilter = world.Filter<UpdateWeaponViewRequestComponent>()
				.End();

			var healthUpdateFilter = world.Filter<UpdateHealthViewRequestComponent>()
				.End();

			var fragCountUpdateFilter = world.Filter<RequestUpdateFragCountComponent>()
				.End();

			if (weaponRequestFilter.GetEntitiesCount() > 0)
			{
				foreach (var weaponRequestEntity in weaponRequestFilter)
				{
					ref var weaponRequest = ref world.GetPool<UpdateWeaponViewRequestComponent>().Get(weaponRequestEntity);
					weaponView.Value.SetWeaponView(weapon.GunConfig, ammoInventory.Get(weapon.GunConfig.Caliber));
				}
			}

			if (ammoRequestFilter.GetEntitiesCount() > 0 || weaponRequestFilter.GetEntitiesCount() > 0)
			{
				foreach (var ammoRequestEntity in ammoRequestFilter)
				{
					ref var ammoRequest = ref world.GetPool<UpdateAmmoViewRequestComponent>().Get(ammoRequestEntity);
					weaponView.Value.UpdateMagazine(weapon.CurrentMagazineCount, ammoInventory.Get(weapon.GunConfig.Caliber));
				}
			}

			if (healthUpdateFilter.GetEntitiesCount() > 0)
			{
				var health = healthPool.Get(player.Value.Entity);
				playerStats.Value.SetHealthValue(health.CurrentHealth);
			}

			if (fragCountUpdateFilter.GetEntitiesCount() > 0)
			{
				playerStats.Value.SetFragCount(fragCount.Value);
			}

			var grenadeUpdateFilter = world.Filter<UpdateGrenadeViewRequestComponent>()
				.End();
			if (grenadeUpdateFilter.GetEntitiesCount() > 0)
			{
				ref var grenadeState = ref world.GetAsSingleton<GrenadeStateComponent>();
				ref var grenadeCounter = ref world.GetAsSingleton<GrenadeCounterUIComponent>();
				if (grenadeCounter.Value != null)
					grenadeCounter.Value.SetCount(grenadeState.Count);
			}

			if (world.TryGetAsSingleton<RequestShowDifficultyComponent>(out var value))
			{
				difficultyView.Value.Show(value.DifficultyLevel, difficulty.DifficultyTimer);
			}else if(world.TryGetAsSingleton<RequestHideDifficultyComponent>(out _) && difficultyView.Value.IsActive)
			{
					difficultyView.Value.Hide();
			}

			if (difficulty.Stage.ShowTimer)
			{
				UnityEngine.Debug.Log($"{nameof(UISystem)}: difficulty.Stage {difficulty.Stage.DifficultyLevel}");
				difficultyView.Value.UpdateView(difficulty.DifficultyTimer / difficulty.Stage.DifficultyTimer, difficulty.DifficultyTimer);
			}
			else
			{
				UnityEngine.Debug.Log($"{nameof(UISystem)}: difficulty.Stage {difficulty.Stage.DifficultyLevel}");
				if (difficultyView.Value.IsActive)
				{
					difficultyView.Value.Hide();
				}
			}

			world.DeleteAllWith<UpdateWeaponViewRequestComponent>();
			world.DeleteAllWith<UpdateAmmoViewRequestComponent>();
			world.DeleteAllWith<RequestOpenWindowComponent>();
			world.DeleteAllWith<UpdateHealthViewRequestComponent>();
			world.DeleteAllWith<RequestUpdateFragCountComponent>();
			world.DeleteAllWith<UpdateGrenadeViewRequestComponent>();
			world.DeleteAllWith<RequestShowDifficultyComponent>();
			world.DeleteAllWith<RequestHideDifficultyComponent>();
		}

		/// <summary>
		/// Reload cycle progress 0..1. Phases run sequentially: first ReloadTime, then ShutteringTime,
		/// so elapsed time = (R - reloadLeft) in the first phase and R + (S - shutterLeft) in the second.
		/// </summary>
		private static float GetReloadProgress(ReloadingComponent reloading, GunConfig config)
		{
			float reload = config.ReloadTime;
			float shutter = config.ShutterTime;
			float total = reload + shutter;
			if (total <= 0f)
				return 1f;

			float elapsed = reloading.ReloadTime > 0
				? reload - reloading.ReloadTime
				: reload + (shutter - reloading.ShutteringTime);

			return UnityEngine.Mathf.Clamp01(elapsed / total);
		}
	}
}
