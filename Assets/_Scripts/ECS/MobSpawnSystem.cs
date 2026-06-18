using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	public class MobSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var spawnRequestPool = world.GetPool<MobSpawnRequestComponent>();
			var filter = world.Filter<MobSpawnRequestComponent>().End();

			if (filter.GetEntitiesCount() == 0)
				return;

			ref var spawnPoints = ref world.GetAsSingleton<SpawnPointsComponent>();
			ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
			ref var playerComponent = ref world.GetAsSingleton<PlayerComponent>();
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;

			var mobComponentPool = world.GetPool<MobComponent>();
			var moveComponentPool = world.GetPool<MoveComponent>();
			var modifierComponentPool = world.GetPool<ModifierOwnerComponent>();
			var healthComponentPool = world.GetPool<HealthComponent>();
			var colliderComponentPool = world.GetPool<ColliderComponent>();
			var pathRecalculationPool = world.GetPool<PathRecalculation>();
			var lookerPool = world.GetPool<LookerAtCamera>();
			var grenadierPool = world.GetPool<GrenadierComponent>();
			var meleeAttackerPool = world.GetPool<MeleeAttackerComponent>();

			float recalcInterval = mainHolder.PathRecalculationInterval;
			float now = Time.time;
			var playerPosition = playerComponent.Value.transform.position;

			foreach (var spawnEntity in filter)
			{
				ref var spawnRequest = ref spawnRequestPool.Get(spawnEntity);
				var mobConfig = spawnRequest.Config;
				var spawnPoint = spawnRequest.SpawnPoint;

				Mob mob = SpawnMob(ref mobPool, mobConfig);

				Vector3 spawnPos = spawnPoint.position;
				if (NavMesh.SamplePosition(spawnPos, out var navHit, 2f, NavMesh.AllAreas))
					spawnPos = navHit.position;
				mob.transform.position = spawnPos;

				var mobEntity = world.NewEntity();

				ref var mobComponent = ref mobComponentPool.Add(mobEntity);
				ref var moveComponent = ref moveComponentPool.Add(mobEntity);
				ref var modifierComponent = ref modifierComponentPool.Add(mobEntity);
				ref var healthComponent = ref healthComponentPool.Add(mobEntity);
				ref var colliderComponent = ref colliderComponentPool.Add(mobEntity);
				ref var pathRecalculationComponent = ref pathRecalculationPool.Add(mobEntity);
				ref var looker = ref lookerPool.Add(mobEntity);

				modifierComponent.Entity = mobEntity;
				modifierComponent.Transform = mob.transform;
				modifierComponent.Modifiers = new();
				mobComponent.Value = mob;
				mobComponent.Config = mobConfig;
				mobComponent.Cooldown = 0;

				Vector3 toPlayer = playerPosition - spawnPos;
				toPlayer.y = 0f;
				moveComponent.Direction = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;
				moveComponent.Speed = mobConfig.Speed;
				moveComponent.Transform = mob.transform;

				healthComponent.CurrentHealth = mobConfig.Health;
				healthComponent.MaxHealth = mobConfig.Health;
				healthComponent.TargetType = mobConfig.TargetType;

				colliderComponent.CollisionType = CollisionType.Mob;
				colliderComponent.Value = mob.Collider;

				pathRecalculationComponent.Interval = recalcInterval;
				// Джиттер: разносим первый пересчёт у пачки мобов, чтобы не пересчитывать всех в один кадр.
				pathRecalculationComponent.LastTime = now - Random.Range(0f, recalcInterval);

				looker.Transform = mob.ValueBar != null ? mob.ValueBar.Transform : null;
				looker.FlatBillboard = true;

				// Моб-гренадёр: тот же моб + поведение броска гранат (GrenadierSystem).
				if (mobConfig is GrenadierMobConfig grenadierConfig)
				{
					ref var grenadier = ref grenadierPool.Add(mobEntity);
					grenadier.Config = grenadierConfig;
					grenadier.State = GrenadierState.Chase;
					grenadier.Timer = 0f;
					grenadier.HasFleeTarget = false;
				}
				// Моб ближнего боя: тот же моб + телеграфированная атака (MeleeAttackerSystem).
				else if (mobConfig is MeleeMobConfig meleeMobConfig)
				{
					ref var attacker = ref meleeAttackerPool.Add(mobEntity);
					attacker.Config = meleeMobConfig;
					attacker.State = MeleeAttackerState.Chase;
					attacker.Timer = 0f;
				}

				InitializeMobGameObject(mob, mobConfig, playerPosition);
				world.DelEntity(spawnEntity);
			}
		}

		/// <summary>
		/// Берёт моба по id из стека пула или инстанцирует нового.
		/// </summary>
		private Mob SpawnMob(ref MobPoolComponent mobPool, MobConfig mobConfig)
		{
			if (mobPool.Pools == null)
				mobPool.Pools = new Dictionary<string, Stack<Mob>>();

			if (mobPool.Pools.TryGetValue(mobConfig.Id, out var stack) && stack.Count > 0)
				return stack.Pop();

			var mob = Object.Instantiate(mobConfig.Prefab, mobPool.Parent);
			mob.SetId(mobConfig.Id);
			return mob;
		}

		private void InitializeMobGameObject(Mob mob, MobConfig mobConfig, Vector2 playerPosition)
		{
			mob.ValueBar.SetMaxValue(mobConfig.Health)
						.ApplyValue(mobConfig.Health)
						.SetVisible(true);

			mob.gameObject.SetActive(true);
		}
	}
}