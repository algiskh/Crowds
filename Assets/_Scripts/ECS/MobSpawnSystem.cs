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

			foreach (var spawnEntity in filter)
			{
				ref var spawnRequest = ref spawnRequestPool.Get(spawnEntity);
				var mobConfig = spawnRequest.Config;
				var spawnPoint = spawnRequest.SpawnPoint;

				Vector3 spawnPos = spawnPoint.position;
				if (NavMesh.SamplePosition(spawnPos, out var navHit, 2f, NavMesh.AllAreas))
					spawnPos = navHit.position;

				CreateMob(world, mobConfig, spawnPos);
				world.DelEntity(spawnEntity);
			}
		}

		/// <summary>
		/// Создаёт сущность моба со всеми базовыми компонентами в заданной позиции и возвращает её id.
		/// Позиция должна быть уже снапнута на navmesh вызывающим. Используется и обычным спауном
		/// (MobSpawnSystem), и групповым (GroupSpawnSystem), чтобы не дублировать инициализацию.
		/// </summary>
		public static int CreateMob(EcsWorld world, MobConfig mobConfig, Vector3 position)
		{
			ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			var playerPosition = world.GetAsSingleton<PlayerComponent>().Value.transform.position;

			float recalcInterval = mainHolder.PathRecalculationInterval;
			float now = Time.time;

			Mob mob = SpawnMob(ref mobPool, mobConfig);
			mob.transform.position = position;

			var mobEntity = world.NewEntity();

			ref var mobComponent = ref world.GetPool<MobComponent>().Add(mobEntity);
			ref var moveComponent = ref world.GetPool<MoveComponent>().Add(mobEntity);
			ref var modifierComponent = ref world.GetPool<ModifierOwnerComponent>().Add(mobEntity);
			ref var healthComponent = ref world.GetPool<HealthComponent>().Add(mobEntity);
			ref var colliderComponent = ref world.GetPool<ColliderComponent>().Add(mobEntity);
			ref var pathRecalculationComponent = ref world.GetPool<PathRecalculation>().Add(mobEntity);
			ref var looker = ref world.GetPool<LookerAtCamera>().Add(mobEntity);

			modifierComponent.Entity = mobEntity;
			modifierComponent.Transform = mob.transform;
			modifierComponent.Modifiers = new();
			mobComponent.Value = mob;
			mobComponent.Config = mobConfig;
			mobComponent.Cooldown = 0;

			Vector3 toPlayer = playerPosition - position;
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
				ref var grenadier = ref world.GetPool<GrenadierComponent>().Add(mobEntity);
				grenadier.Config = grenadierConfig;
				grenadier.State = GrenadierState.Chase;
				grenadier.Timer = 0f;
				grenadier.HasFleeTarget = false;
			}
			// Моб ближнего боя: тот же моб + телеграфированная атака (MeleeAttackerSystem).
			else if (mobConfig is MeleeMobConfig meleeMobConfig)
			{
				ref var attacker = ref world.GetPool<MeleeAttackerComponent>().Add(mobEntity);
				attacker.Config = meleeMobConfig;
				attacker.State = MeleeAttackerState.Chase;
				attacker.Timer = 0f;
			}

			InitializeMobGameObject(mob, mobConfig, playerPosition);
			return mobEntity;
		}

		/// <summary>
		/// Берёт моба по id из стека пула или инстанцирует нового.
		/// </summary>
		private static Mob SpawnMob(ref MobPoolComponent mobPool, MobConfig mobConfig)
		{
			if (mobPool.Pools == null)
				mobPool.Pools = new Dictionary<string, Stack<Mob>>();

			if (mobPool.Pools.TryGetValue(mobConfig.Id, out var stack) && stack.Count > 0)
				return stack.Pop();

			var mob = Object.Instantiate(mobConfig.Prefab, mobPool.Parent);
			mob.SetId(mobConfig.Id);
			return mob;
		}

		private static void InitializeMobGameObject(Mob mob, MobConfig mobConfig, Vector2 playerPosition)
		{
			mob.ValueBar.SetMaxValue(mobConfig.Health)
						.ApplyValue(mobConfig.Health)
						.SetVisible(true);

			mob.gameObject.SetActive(true);
		}
	}
}