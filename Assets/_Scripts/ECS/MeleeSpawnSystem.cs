using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class MeleeSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var meleeSpawnPool = world.GetPool<RequestMeleeComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var moveComponentPool = world.GetPool<MoveComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();

			var damageRequestPool = world.GetPool<RequestDamageComponent>();

			var filter = world.Filter<RequestMeleeComponent>().End();
			foreach (var entity in filter)
			{
				ref var spawnRequest = ref meleeSpawnPool.Get(entity);

				if (spawnRequest.Delay > 0)
				{
					spawnRequest.Delay -= Time.deltaTime;
					continue;
				}

				var healthFilter = world.Filter<HealthComponent>().End();
				foreach (var targetEntity in healthFilter)
				{
					ref var health = ref healthPool.Get(targetEntity);

					if (IsInRadius(targetEntity, moveComponentPool, spawnRequest))
					{
						if (spawnRequest.Config.TargetType != health.TargetType)
						{
							continue;
						}

						damageRequestPool.Add(world.NewEntity()) = new RequestDamageComponent
						{
							TargetEntity = targetEntity,
							Damage = spawnRequest.Config.Damage
						};


						if (modifierPool.Has(targetEntity))
						{
							ref var modifier = ref modifierPool.Get(targetEntity);
							modifierPool.Add(targetEntity) = modifier;
						}

					}
					meleeSpawnPool.Del(entity);
				}
			}
		}

		private bool IsInRadius(int targetEntity, EcsPool<MoveComponent> moveComponentPool, RequestMeleeComponent meleeRequest)
		{
			if (moveComponentPool.Has(targetEntity))
			{
				ref var moveComponent = ref moveComponentPool.Get(targetEntity);
				var distance = (moveComponent.Transform.position - meleeRequest.Position).magnitude;

				if (distance <= meleeRequest.Config.Radius)
				{
					return true;
				}
			}
			return false;
		}
	}
}