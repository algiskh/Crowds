using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class PlayerSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var playerPool = world.GetPool<PlayerComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			ref var playerInput = ref world.GetAsSingleton<PlayerInputComponent>();

			Vector3 input = playerInput.Move;
			Vector3 previousInput = playerInput.PreviousMove;
			ref var muzzle = ref world.GetAsSingleton<WeaponComponent>();

			var filter = world.Filter<PlayerComponent>().Inc<HealthComponent>().End();
			foreach (var entity in filter)
			{
				ref var player = ref playerPool.Get(entity);
				bool isIdle = Mathf.Approximately(input.x, 0f) && Mathf.Approximately(input.z, 0f);

				var fireRequestPool = world.GetPool<RequestFireComponent>();
				if (playerInput.IsFiring)
				{
					Debug.Log($"Try to fire");
					var fireEntity = world.NewEntity();
					ref var requestFireComponent = ref fireRequestPool.Add(fireEntity);
				}

				if (playerInput.IsMeleeing)
				{
					playerInput.MeleeCooldown = player.Value.MeleeConfig.Cooldown;
					Debug.Log($"Try to melee");
					var meleeEntity = world.NewEntity();
					ref var requestMeleeComponent = ref world.GetPool<RequestMeleeComponent>().Add(meleeEntity);
					requestMeleeComponent.Position = player.Value.transform.GetForwardPosition(player.Value.MeleeConfig.Range);
					requestMeleeComponent.SourceEntity = entity;
					if (player.Value.MeleeConfig == null)
					{
						Debug.LogError($"Player {entity} has no melee config assigned!");
					}
					requestMeleeComponent.Config = player.Value.MeleeConfig;
					requestMeleeComponent.Delay = player.Value.MeleeConfig.Delay;
				}
			}
		}
	}
}
