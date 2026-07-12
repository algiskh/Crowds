using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class BulletOverlapSystem : IEcsRunSystem
	{
		private const int BUFFER_SIZE = 32;

		// Эффективный «радиус тела» игрока для попадания Enemy-пуль. У игрока нет
		// зарегистрированного ColliderComponent, поэтому цель проверяется по дистанции
		// (bullet.Radius + это) — как контактный урон мобов в CollisionSystem.PlayerVsMob.
		private const float PLAYER_HIT_RADIUS = 0.4f;

		private readonly Collider[] _overlapBuffer = new Collider[BUFFER_SIZE];
		private readonly RaycastHit[] _raycastBuffer = new RaycastHit[BUFFER_SIZE];

		// Переиспользуется каждый кадр: collider → mob entity id.
		private readonly Dictionary<Collider, int> _mobColliderMap = new Dictionary<Collider, int>(64);
		// collider → breakable entity id (разрушаемое окружение).
		private readonly Dictionary<Collider, int> _breakableColliderMap = new Dictionary<Collider, int>(32);

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var bulletPool = world.GetPool<BulletComponent>();
			var overlapPool = world.GetPool<BulletOverlapComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();
			var mobPool = world.GetPool<MobComponent>();

			var bulletFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().End();
			// Нет пуль в полёте — не перестраиваем коллайдер-карты мобов/разрушаемого (экономия O(мобов)/кадр).
			if (bulletFilter.GetEntitiesCount() == 0)
				return;

			int layerMask = ~0;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				layerMask = mainHolder.Value.DamageableLayerMask;

			// Единственная цель Enemy-пуль — игрок. Проверяем по дистанции (у игрока нет коллайдер-карты).
			bool hasPlayer = world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton) && playerSingleton.Value != null;
			Vector3 playerPos = hasPlayer ? playerSingleton.Value.transform.position : default;
			float dt = Time.deltaTime;

			// --- Один раз за кадр строим collider → entity карту для всех мобов.
			_mobColliderMap.Clear();
			var mobFilter = world.Filter<MobComponent>().Inc<ColliderComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var col = ref colliderPool.Get(mobEntity);
				if (col.CollisionType == CollisionType.Mob && col.Value != null)
					_mobColliderMap[col.Value] = mobEntity;
			}

			// --- И карту разрушаемых объектов окружения (тем же паттерном).
			_breakableColliderMap.Clear();
			var breakableFilter = world.Filter<BreakableComponent>().Inc<ColliderComponent>().End();
			foreach (var breakableEntity in breakableFilter)
			{
				ref var col = ref colliderPool.Get(breakableEntity);
				if (col.CollisionType == CollisionType.Breakable && col.Value != null)
					_breakableColliderMap[col.Value] = breakableEntity;
			}

			foreach (var bulletEntity in bulletFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				ref var move = ref movePool.Get(bulletEntity);
				if (move.Transform == null) continue;

				var position = move.Transform.position;
				bool isEnemyBullet = bullet.Team == BulletTeam.Enemy;
				int hitCount = 0;

				switch (bullet.CheckType)
				{
					case BulletCheckType.OverlapSphere:
						hitCount = Physics.OverlapSphereNonAlloc(position, bullet.Radius, _overlapBuffer, layerMask, QueryTriggerInteraction.Collide);
						break;

					case BulletCheckType.OverlapBox:
						var halfExtents = new Vector3(bullet.Radius, bullet.Radius, bullet.Radius);
						hitCount = Physics.OverlapBoxNonAlloc(position, halfExtents, _overlapBuffer, move.Transform.rotation, layerMask, QueryTriggerInteraction.Collide);
						break;

					case BulletCheckType.Raycast:
						var direction = move.Transform.forward;
						// Для быстрых пуль: путь за кадр + радиус.
						float distance = Mathf.Max(bullet.Radius, move.Speed * Time.deltaTime + bullet.Radius);
						int rayHits = Physics.RaycastNonAlloc(position, direction, _raycastBuffer, distance, layerMask, QueryTriggerInteraction.Collide);
						hitCount = Mathf.Min(rayHits, _overlapBuffer.Length);
						for (int i = 0; i < hitCount; i++)
							_overlapBuffer[i] = _raycastBuffer[i].collider;
						break;
				}

				bool hasAnyMobHit = false;
				bool hasAnyBreakableHit = false;
				bool playerHit = false;
				Unity.Collections.FixedList128Bytes<int> mobHits = default;
				Unity.Collections.FixedList128Bytes<int> breakableHits = default;

				for (int i = 0; i < hitCount; i++)
				{
					var col = _overlapBuffer[i];
					if (col == null) continue;
					// Player-пули бьют мобов; Enemy-пули — нет (без дружественного огня по мобам).
					if (!isEnemyBullet && _mobColliderMap.TryGetValue(col, out var mobEntityId))
					{
						// FixedList128Bytes<int>: ~31 элементов max — с запасом для одной пули.
						if (mobHits.Length < mobHits.Capacity)
							mobHits.Add(mobEntityId);
						hasAnyMobHit = true;
					}
					else if (_breakableColliderMap.TryGetValue(col, out var breakableEntityId))
					{
						// Разрушаемое окружение — укрытие для пуль обеих сторон.
						if (breakableHits.Length < breakableHits.Capacity)
							breakableHits.Add(breakableEntityId);
						hasAnyBreakableHit = true;
					}
				}

				// Enemy-пуля vs игрок: единственная цель. Top-down (считаем горизонтально: пуля летит на
				// высоте дула, игрок в origin у земли) + свипт-отрезок за кадр против тоннелирования быстрых
				// пуль — берём ближайшую точку отрезка [prev→cur] к игроку.
				if (isEnemyBullet && hasPlayer)
				{
					Vector3 cur = position; cur.y = 0f;
					Vector3 pl = playerPos; pl.y = 0f;
					Vector3 seg = move.Direction * (move.Speed * dt); seg.y = 0f;
					Vector3 prev = cur - seg;
					float segLenSqr = seg.sqrMagnitude;
					Vector3 closest = cur;
					if (segLenSqr > 1e-6f)
					{
						float t = Mathf.Clamp01(Vector3.Dot(pl - prev, seg) / segLenSqr);
						closest = prev + seg * t;
					}
					float reach = bullet.Radius + PLAYER_HIT_RADIUS;
					if ((pl - closest).sqrMagnitude <= reach * reach)
						playerHit = true;
				}

				if (hasAnyMobHit || hasAnyBreakableHit || playerHit)
				{
					ref var overlap = ref overlapPool.Has(bulletEntity)
						? ref overlapPool.Get(bulletEntity)
						: ref overlapPool.Add(bulletEntity);
					overlap.MobHits = mobHits;
					overlap.BreakableHits = breakableHits;
					overlap.PlayerHit = playerHit;
				}
				else if (overlapPool.Has(bulletEntity))
				{
					overlapPool.Del(bulletEntity);
				}
			}
		}
	}
}
