using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Спаунит разрушаемое окружение по запросу (RequestSpawnBreakableComponent) в заданной точке —
	/// конфиг задаётся напрямую или по id (через MainHolder.BreakableConfigHolder). Инстансы берутся из
	/// пула по id (как мобы/эффекты) и возвращаются туда при Vanish-разрушении. Регистрацию ECS-состояния
	/// разделяет со сценовыми объектами (RegisterBreakable), которые EntryPoint находит на старте.
	/// См. Docs/BreakableFeature.md.
	/// </summary>
	public class BreakableSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var pool = world.GetPool<RequestSpawnBreakableComponent>();
			var filter = world.Filter<RequestSpawnBreakableComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;

			BreakableConfigHolder holder = null;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				holder = mainHolder.Value.BreakableConfigHolder;

			foreach (var entity in filter)
			{
				ref var request = ref pool.Get(entity);

				var config = request.Config;
				if (config == null && holder != null && !string.IsNullOrEmpty(request.Id))
					config = holder.GetConfigById(request.Id);

				if (config != null && config.Prefab != null)
					CreateBreakable(world, config, request.Position, request.Rotation);
				else
					Debug.LogWarning($"[BreakableSpawnSystem] Не могу заспаунить breakable (Id='{request.Id}') — нет конфига/префаба.");

				world.DelEntity(entity);
			}
		}

		/// <summary>
		/// Создаёт разрушаемый объект в точке: берёт инстанс из пула (или инстанцирует префаб конфига),
		/// сбрасывает его в целое состояние и регистрирует ECS-сущность. Возвращает id сущности.
		/// </summary>
		public static int CreateBreakable(EcsWorld world, BreakableConfig config, Vector3 position, float rotation = 0f)
		{
			ref var breakablePool = ref world.GetAsSingleton<BreakablePoolComponent>();
			var view = Pop(ref breakablePool, config);

			view.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation, 0f));
			view.ResetForSpawn();

			int entity = RegisterBreakable(world, view, config, pooled: true);

			// Появилось новое carving-препятствие — заставляем ближних мобов перепроложить путь.
			BreakableSystem.RequestNearbyRepath(world, position);
			return entity;
		}

		/// <summary>
		/// Навешивает ECS-состояние (BreakableComponent + HealthComponent + ColliderComponent) на уже
		/// существующий Breakable — общая логика для сценовых (pooled=false) и рантайм-объектов (pooled=true).
		/// </summary>
		public static int RegisterBreakable(EcsWorld world, Breakable view, BreakableConfig config, bool pooled)
		{
			int entity = world.NewEntity();

			ref var breakable = ref world.GetPool<BreakableComponent>().Add(entity);
			breakable.Value = view;
			breakable.Config = config;
			breakable.ContactCooldown = 0f;
			breakable.Pooled = pooled;

			ref var health = ref world.GetPool<HealthComponent>().Add(entity);
			health.MaxHealth = config.MaxHealth;
			health.CurrentHealth = config.MaxHealth;
			health.TargetType = TargetType.Enviroment;

			ref var collider = ref world.GetPool<ColliderComponent>().Add(entity);
			collider.Value = view.Collider;
			collider.CollisionType = CollisionType.Breakable;

			return entity;
		}

		private static Breakable Pop(ref BreakablePoolComponent pool, BreakableConfig config)
		{
			pool.Pools ??= new Dictionary<string, Stack<Breakable>>();

			var id = config.Id;
			if (!string.IsNullOrEmpty(id) && pool.Pools.TryGetValue(id, out var stack) && stack.Count > 0)
				return stack.Pop();

			return Object.Instantiate(config.Prefab, pool.Parent);
		}
	}
}
