using Leopotam.EcsLite;
using UnityEngine;
namespace ECS
{
	public class DamageSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var mobSpawnPool = ref world.GetAsSingleton<MobPoolComponent>();
			var requestDamagePool = world.GetPool<RequestDamageComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var healthPool = world.GetPool<HealthComponent>();

			#region Handling DamageRequests
			var filter = world.Filter<RequestDamageComponent>().End();
			foreach (var entity in filter)
			{
				ref var requestDamageComponent = ref requestDamagePool.Get(entity);
				var target = requestDamageComponent.TargetEntity;
				ref var healthComponent = ref healthPool.Get(target);
				// Apply damage to health
				healthComponent.CurrentHealth -= requestDamageComponent.Damage;
				ref var mob = ref mobPool.Get(target);
				mob.Value.ValueBar
					.ApplyValue(healthComponent.CurrentHealth);

				// Check if health is below zero
				if (healthComponent.CurrentHealth <= 0)
				{
					healthComponent.CurrentHealth = 0;

					ref var mobLoot = ref world.CreateSimpleEntity<RequestLootSpawn>();
					mobLoot.PossibleLoots = mob.Config.PossibleLoots;
					mobLoot.Position = mob.Value.transform.position;
				}
				requestDamagePool.Del(entity);
			}
			#endregion

			#region Handling zombie health
			var mobFilter = world.Filter<MobComponent>().Inc<HealthComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				if (!mobPool.Has(mobEntity) || !healthPool.Has(mobEntity))
					continue;
				ref var mobComponent = ref mobPool.Get(mobEntity);
				ref var healthComponent = ref healthPool.Get(mobEntity);

				var position = mobComponent.Value.transform.position;

				if (healthComponent.CurrentHealth <= 0)
				{
					mobSpawnPool.Value.Add(mobComponent.Value);
					//mobComponent.Value.SimpleAnimator.Stop();
					mobComponent.Value.gameObject.SetActive(false);

					//Request Effect
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = "zombie_dead";
					effectRequest.Position = position;

					world.DelEntity(mobEntity);
				}
			}
			#endregion
		}
	}
}