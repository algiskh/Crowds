using Leopotam.EcsLite;
using System.Diagnostics;

namespace ECS
{
	public class UISystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var fragCount = ref world.GetAsSingleton<FragCountComponent>();
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var weaponView = ref world.GetAsSingleton<WeaponUIViewComponent>();
			ref var playerStats = ref world.GetAsSingleton<PlayerStatsComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			ref var failedWindow =  ref world.GetAsSingleton<FailWindowComponent>();
			ref var difficultyView = ref world.GetAsSingleton<DifficultyTimerUIComponent>();
			ref var difficulty = ref world.GetAsSingleton<DifficultyComponent>();

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
				}
				world.DelEntity(requestEntity);
			}

			if (reloading.ReloadTime > 0)
			{
				weaponView.Value.ShowReloading((weapon.GunConfig.ReloadTime - reloading.ReloadTime) / 1);
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
					weaponView.Value.SetWeaponView(weapon.GunConfig, weapon.AmmoCount);
				}
			}

			if (ammoRequestFilter.GetEntitiesCount() > 0 || weaponRequestFilter.GetEntitiesCount() > 0)
			{
				foreach (var ammoRequestEntity in ammoRequestFilter)
				{
					ref var ammoRequest = ref world.GetPool<UpdateAmmoViewRequestComponent>().Get(ammoRequestEntity);
					weaponView.Value.UpdateMagazine(weapon.CurrentMagazineCount, weapon.AmmoCount);
				}
			}

			if (healthUpdateFilter.GetEntitiesCount() > 0)
			{
				var health = healthPool.Get(player.Value.Entity);
				playerStats.Value.SetHealthValue(health.CurrentHealth);
				UnityEngine.Debug.Log($"Change health to {health.CurrentHealth}");
			}

			if (fragCountUpdateFilter.GetEntitiesCount() > 0)
			{
				playerStats.Value.SetFragCount(fragCount.Value);
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
			world.DeleteAllWith<RequestShowDifficultyComponent>();
			world.DeleteAllWith<RequestHideDifficultyComponent>();
		}
	}
}