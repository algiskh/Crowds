using Leopotam.EcsLite;

namespace ECS
{
	public class UISystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			ref var weaponView = ref world.GetAsSingleton<WeaponUIViewComponent>();
			ref var reloading = ref world.GetAsSingleton<ReloadingComponent>();


			var requestPool = world.GetPool<RequestOpenWindowComponent>();
			var filter = world.Filter<RequestOpenWindowComponent>()
				.End();
			foreach (var requestEntity in filter)
			{
				ref var request = ref requestPool.Get(requestEntity);
				if (request.WindowType is WindowType.FailWindow)
				{
					ref var failWindow = ref world.GetAsSingleton<FailWindowComponent>();
					failWindow.Value.Open();
				}
				world.DelEntity(requestEntity);
			}

			if (reloading.ReloadTime > 0)
			{
				weaponView.Value.ShowReloading(1 / (reloading.ReloadTime / weapon.ReloadTime));
			}



			var ammoRequestFilter = world.Filter<UpdateAmmoViewRequestComponent>()
				.End();
			
			var weaponRequestFilter = world.Filter<UpdateWeaponViewRequestComponent>()
				.End();

			if(weaponRequestFilter.GetEntitiesCount() > 0)
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

			world.DeleteAllWith<UpdateWeaponViewRequestComponent>();
			world.DeleteAllWith<UpdateAmmoViewRequestComponent>();
			world.DeleteAllWith<RequestOpenWindowComponent>();
		}
	}
}