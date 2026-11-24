using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Breakable environment. Damage arrives via the regular RequestDamageComponent (from bullets/explosions/melee —
	/// gated by the source at the damage sites), and this system pass:
	///  1) deals mob contact damage (MobContact source) — on a cooldown, to avoid spamming;
	///  2) at HP<=0 plays out the destruction: staged effects at mesh points, scatters loot with
	///     spread, applies the outcome (Vanish/Debris) and makes nearby mobs repath
	///     (the carving obstacle disappeared/changed — no navmesh rebaking).
	/// Runs right after DamageSystem (HP already applied) and before LootSystem/EffectsSystem
	/// (so their requests are processed the same frame). See Docs/BreakableFeature.md.
	/// </summary>
	public sealed class BreakableSystem : IEcsRunSystem
	{
		private const int CONTACT_BUFFER = 16;
		private const float CONTACT_COOLDOWN = 0.5f;   // seconds between contact-damage ticks per object
		private const float REPATH_RADIUS = 8f;        // radius in which mobs get a force-repath on destruction
		private const float GOLDEN_ANGLE = 2.399963f;  // radians (~137.5°) — even loot spread

		private readonly Collider[] _contactBuffer = new Collider[CONTACT_BUFFER];
		private readonly Dictionary<Collider, int> _mobColliderMap = new Dictionary<Collider, int>(64);

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			var breakablePool = world.GetPool<BreakableComponent>();
			var healthPool = world.GetPool<HealthComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();

			var filter = world.Filter<BreakableComponent>().Inc<HealthComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;

			int mobMask = ~0;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				mobMask = mainHolder.Value.MobLayerMask.value;

			HandleMobContact(world, filter, breakablePool, mobPool, colliderPool, mobMask);

			// Destruction: deleting the entity during filter iteration is fine in this EcsLite version
			// (same pattern as in DamageSystem "Handling zombie health").
			foreach (var entity in filter)
			{
				ref var health = ref healthPool.Get(entity);
				if (health.CurrentHealth > 0f)
					continue;

				ref var breakable = ref breakablePool.Get(entity);
				Destroy(world, entity, ref breakable);
			}
		}

		// MobContact source: mobs "breaking through" the object by contact. We check only objects
		// with this source enabled; the mob map is built lazily, once per frame.
		private void HandleMobContact(EcsWorld world, EcsFilter filter, EcsPool<BreakableComponent> breakablePool,
			EcsPool<MobComponent> mobPool, EcsPool<ColliderComponent> colliderPool, int mobMask)
		{
			bool mapBuilt = false;

			foreach (var entity in filter)
			{
				ref var breakable = ref breakablePool.Get(entity);
				if (breakable.Config == null || !breakable.Config.CanBeDamagedBy(BreakableDamageSources.MobContact))
					continue;

				if (breakable.ContactCooldown > 0f)
				{
					breakable.ContactCooldown -= Time.deltaTime;
					continue;
				}

				var view = breakable.Value;
				if (view == null || view.Collider == null)
					continue;

				if (!mapBuilt)
				{
					BuildMobColliderMap(world, mobPool, colliderPool);
					mapBuilt = true;
				}
				if (_mobColliderMap.Count == 0)
					continue;

				var bounds = view.Collider.bounds;
				int hits = Physics.OverlapSphereNonAlloc(
					bounds.center, bounds.extents.magnitude, _contactBuffer, mobMask, QueryTriggerInteraction.Collide);

				for (int i = 0; i < hits; i++)
				{
					var col = _contactBuffer[i];
					if (col == null) continue;
					if (!_mobColliderMap.TryGetValue(col, out var mobEntity)) continue;
					if (!mobPool.Has(mobEntity)) continue;

					ref var mob = ref mobPool.Get(mobEntity);
					float dmg = mob.Config != null ? mob.Config.Damage : 0f;
					if (dmg <= 0f) continue;

					ref var damage = ref world.CreateSimpleEntity<RequestDamageComponent>();
					damage.Target = world.PackEntity(entity);
					damage.Damage = dmg;

					breakable.ContactCooldown = CONTACT_COOLDOWN;
					break;
				}
			}
		}

		private void BuildMobColliderMap(EcsWorld world, EcsPool<MobComponent> mobPool, EcsPool<ColliderComponent> colliderPool)
		{
			_mobColliderMap.Clear();
			var mobFilter = world.Filter<MobComponent>().Inc<ColliderComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var col = ref colliderPool.Get(mobEntity);
				if (col.CollisionType == CollisionType.Mob && col.Value != null)
					_mobColliderMap[col.Value] = mobEntity;
			}
		}

		private void Destroy(EcsWorld world, int entity, ref BreakableComponent breakable)
		{
			var config = breakable.Config;
			var view = breakable.Value;
			Vector3 center = view != null ? view.transform.position : Vector3.zero;

			// 1) Staged destruction effects (each at its own mesh point, with its own delay).
			if (config != null && config.DestructionEffects != null)
			{
				foreach (var fx in config.DestructionEffects)
				{
					if (fx == null || string.IsNullOrEmpty(fx.EffectId))
						continue;
					ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
					effectRequest.EffectId = fx.EffectId;
					effectRequest.Position = view != null ? view.GetEffectPoint(fx.PointIndex) : center;
					effectRequest.Delay = fx.Delay;
				}
			}

			// 1b) Optional radial damage on destruction (barrels etc.): reuse the explosion
			//     system — damage falls off from center to edge, hits mobs, the player and chains into
			//     other breakables. The entity itself is deleted this frame, so the explosion won't touch it.
			if (config != null && config.DamageOnDestruction && config.DamageRadius > 0f)
			{
				ref var explosion = ref world.CreateSimpleEntity<RequestExplosionComponent>();
				explosion.Position = center;
				explosion.Radius = config.DamageRadius;
				explosion.MaxDamage = config.MaxDamage;
				explosion.MinDamage = config.MinDamage;
				explosion.MobDamageScale = config.MobDamageScale;
				explosion.PlayerDamageScale = config.PlayerDamageScale;
				explosion.EffectId = config.DamageEffectId;
				explosion.Delay = 0f;
			}

			// 2) Loot with spread: the Vogel spiral (r = spread*sqrt(i)) gives a minimum distance of ~spread
			//    between neighboring items. Each request independently passes the drop table to LootSystem.
			//    The table/count may be overridden per instance (Breakable._overrideLoot), e.g. a bonus crate.
			bool lootOverride = view != null && view.OverridesLoot;
			var lootTable = lootOverride ? view.LootTable : config != null ? config.LootTable : null;
			int lootCount = lootOverride ? view.LootCount : config != null ? config.LootCount : 0;
			if (lootTable != null && lootTable.Length > 0 && lootCount > 0)
			{
				float spread = config != null ? Mathf.Max(0f, config.LootSpread) : 0f;
				for (int i = 0; i < lootCount; i++)
				{
					Vector3 pos = center;
					if (spread > 0f)
					{
						float r = spread * Mathf.Sqrt(i);
						float angle = i * GOLDEN_ANGLE;
						pos += new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
					}

					ref var lootRequest = ref world.CreateSimpleEntity<RequestLootSpawn>();
					lootRequest.Source = RequestSpawnSource.Breakable;
					// The Breakable entity is deleted the same frame — don't attach a source (LootSystem
					// doesn't dereference SourceEntity for non-AdditionalSpawn sources).
					lootRequest.SourceEntity = -1;
					lootRequest.PossibleLoots = lootTable;
					lootRequest.Position = pos;
				}
			}

			// 3) Outcome: debris (optionally keeps carving the navmesh) or full disappearance (navmesh is freed).
			if (view != null)
			{
				if (config != null && config.AfterDestruction == AfterDestruction.Debris)
				{
					view.ShowDebris(config.DebrisKeepsObstacle);
				}
				else
				{
					view.Vanish();
					// Runtime objects (from the pool) go back to the pool for reuse; scene ones just go dark.
					if (breakable.Pooled && config != null && !string.IsNullOrEmpty(config.Id))
						ReturnToPool(world, config.Id, view);
				}
			}

			// 4) Force-repath nearby mobs: the carving obstacle disappeared/changed — let them recalculate.
			RequestNearbyRepath(world, center);

			// 5) The entity is no longer needed: the GameObject lives on as debris, deactivated or in the pool.
			world.DelEntity(entity);
		}

		private static void ReturnToPool(EcsWorld world, string id, Breakable view)
		{
			ref var pool = ref world.GetAsSingleton<BreakablePoolComponent>();
			pool.Pools ??= new Dictionary<string, Stack<Breakable>>();
			if (!pool.Pools.TryGetValue(id, out var stack))
			{
				stack = new Stack<Breakable>();
				pool.Pools[id] = stack;
			}
			if (pool.Parent != null)
				view.transform.SetParent(pool.Parent);
			stack.Push(view);
		}

		// Makes mobs within REPATH_RADIUS repath (a carving obstacle appeared/disappeared).
		// Public static — called both on destruction and on runtime spawn (BreakableSpawnSystem).
		public static void RequestNearbyRepath(EcsWorld world, Vector3 center)
		{
			var mobPool = world.GetPool<MobComponent>();
			var recalcRequestPool = world.GetPool<PathRecalculationRequest>();
			var mobFilter = world.Filter<MobComponent>().Inc<PathRecalculation>().End();
			float sqrRadius = REPATH_RADIUS * REPATH_RADIUS;

			foreach (var mobEntity in mobFilter)
			{
				if (!mobPool.Has(mobEntity)) continue;
				ref var mob = ref mobPool.Get(mobEntity);
				if (mob.Value == null) continue;
				if ((mob.Value.transform.position - center).sqrMagnitude > sqrRadius) continue;
				if (!recalcRequestPool.Has(mobEntity))
					recalcRequestPool.Add(mobEntity);
			}
		}
	}
}
