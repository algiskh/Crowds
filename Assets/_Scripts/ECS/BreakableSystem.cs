using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Разрушаемое окружение. Урон приходит через обычный RequestDamageComponent (от пуль/взрывов/ближнего
	/// боя — гейтится источником в местах нанесения), а этот системный проход:
	///  1) наносит урон от контакта мобов (источник MobContact) — по кулдауну, чтобы не спамить;
	///  2) на HP<=0 разыгрывает разрушение: ступенчатые эффекты по точкам меша, рассыпает лут с
	///     разбросом, применяет исход (Vanish/Debris) и заставляет ближних мобов перестроить путь
	///     (carving-препятствие исчезло/изменилось — без запекания navmesh).
	/// Регистрируется сразу после DamageSystem (HP уже применён) и до LootSystem/EffectsSystem
	/// (чтобы их запросы обработались тем же кадром). См. Docs/BreakableFeature.md.
	/// </summary>
	public sealed class BreakableSystem : IEcsRunSystem
	{
		private const int CONTACT_BUFFER = 16;
		private const float CONTACT_COOLDOWN = 0.5f;   // сек между тиками контактного урона по объекту
		private const float REPATH_RADIUS = 8f;        // радиус, в котором мобы получают форс-репас при разрушении
		private const float GOLDEN_ANGLE = 2.399963f;  // радианы (~137.5°) — равномерный разброс лута

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

			// Разрушение: удаление сущности во время обхода фильтра допустимо в этой версии EcsLite
			// (тот же паттерн, что в DamageSystem «Handling zombie health»).
			foreach (var entity in filter)
			{
				ref var health = ref healthPool.Get(entity);
				if (health.CurrentHealth > 0f)
					continue;

				ref var breakable = ref breakablePool.Get(entity);
				Destroy(world, entity, ref breakable);
			}
		}

		// Источник MobContact: мобы, «пробивающие» объект контактом. Проверяем только объекты,
		// у которых включён этот источник; карту мобов строим лениво, один раз за кадр.
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
					damage.TargetEntity = entity;
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

			// 1) Ступенчатые эффекты разрушения (каждый в своей точке меша, со своей задержкой).
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

			// 1b) Опциональный радиальный урон при разрушении (бочки и т.п.): переиспользуем систему
			//     взрывов — спад урона от центра к краю, бьёт мобов, игрока и цепочкой другие breakable.
			//     Сама сущность удаляется этим кадром, поэтому себя же взрывом не заденет.
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

			// 2) Лут с разбросом: спираль Фогеля (r = spread*sqrt(i)) даёт минимальную дистанцию ~spread
			//    между соседними предметами. Каждый запрос независимо прокидывает drop-таблицу в LootSystem.
			if (config != null && config.LootTable != null && config.LootTable.Length > 0 && config.LootCount > 0)
			{
				float spread = Mathf.Max(0f, config.LootSpread);
				for (int i = 0; i < config.LootCount; i++)
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
					// Breakable-сущность удаляется этим же кадром — источник не привязываем (LootSystem
					// не разыменовывает SourceEntity для не-AdditionalSpawn источников).
					lootRequest.SourceEntity = -1;
					lootRequest.PossibleLoots = config.LootTable;
					lootRequest.Position = pos;
				}
			}

			// 3) Исход: debris (опц. продолжает карвить navmesh) или полное исчезновение (navmesh освобождается).
			if (view != null)
			{
				if (config != null && config.AfterDestruction == AfterDestruction.Debris)
				{
					view.ShowDebris(config.DebrisKeepsObstacle);
				}
				else
				{
					view.Vanish();
					// Рантайм-объект (из пула) возвращаем в пул для переиспользования; сценовый просто гаснет.
					if (breakable.Pooled && config != null && !string.IsNullOrEmpty(config.Id))
						ReturnToPool(world, config.Id, view);
				}
			}

			// 4) Форс-репас ближних мобов: carving-препятствие исчезло/изменилось — пусть перепроложат путь.
			RequestNearbyRepath(world, center);

			// 5) Сущность больше не нужна: GameObject живёт как debris, деактивирован или лежит в пуле.
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

		// Заставляет мобов в радиусе REPATH_RADIUS перепроложить путь (появилось/исчезло carving-препятствие).
		// Публичный статик — вызывается и при разрушении, и при рантайм-спауне (BreakableSpawnSystem).
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
