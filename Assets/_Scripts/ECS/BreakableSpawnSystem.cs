using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Spawns breakable environment on request (RequestSpawnBreakableComponent) at a given point —
	/// the config is given directly or by id (via MainHolder.BreakableConfigHolder). Instances are taken
	/// from a pool by id (like mobs/effects) and returned there on Vanish destruction. ECS-state registration
	/// is shared with scene objects (RegisterBreakable), which EntryPoint finds at startup.
	/// See Docs/BreakableFeature.md.
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
					Debug.LogWarning($"[BreakableSpawnSystem] Can't spawn breakable (Id='{request.Id}') — no config/prefab.");

				world.DelEntity(entity);
			}
		}

		/// <summary>
		/// Creates a breakable object at a point: takes an instance from the pool (or instantiates the config prefab),
		/// resets it to the intact state and registers an ECS entity. Returns the entity id.
		/// </summary>
		public static int CreateBreakable(EcsWorld world, BreakableConfig config, Vector3 position, float rotation = 0f)
		{
			ref var breakablePool = ref world.GetAsSingleton<BreakablePoolComponent>();
			var view = Pop(ref breakablePool, config);

			view.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotation, 0f));
			view.ResetForSpawn();

			int entity = RegisterBreakable(world, view, config, pooled: true);

			// A new carving obstacle appeared — make nearby mobs recalculate their paths.
			BreakableSystem.RequestNearbyRepath(world, position);
			return entity;
		}

		/// <summary>
		/// Attaches the ECS state (BreakableComponent + HealthComponent + ColliderComponent) to an
		/// existing Breakable — shared logic for scene objects (pooled=false) and runtime objects (pooled=true).
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
