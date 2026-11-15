using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class BulletOverlapSystem : IEcsRunSystem
	{
		private const int BUFFER_SIZE = 32;

		// Effective "body radius" of the player for Enemy-bullet hits. The player has no
		// registered ColliderComponent, so the target is checked by distance
		// (bullet.Radius + this) — like mob contact damage in CollisionSystem.PlayerVsMob.
		private const float PLAYER_HIT_RADIUS = 0.4f;

		private readonly Collider[] _overlapBuffer = new Collider[BUFFER_SIZE];
		private readonly RaycastHit[] _raycastBuffer = new RaycastHit[BUFFER_SIZE];

		// Reused every frame: collider → mob entity id.
		private readonly Dictionary<Collider, int> _mobColliderMap = new Dictionary<Collider, int>(64);
		// collider → breakable entity id (destructible environment).
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
			// No bullets in flight — don't rebuild the mob/breakable collider maps (saves O(mobs)/frame).
			if (bulletFilter.GetEntitiesCount() == 0)
				return;

			int layerMask = ~0;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				layerMask = mainHolder.Value.DamageableLayerMask;

			// The only target of Enemy bullets — the player. Checked by distance (the player has no collider map).
			bool hasPlayer = world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton) && playerSingleton.Value != null;
			Vector3 playerPos = hasPlayer ? playerSingleton.Value.transform.position : default;
			float dt = world.GameDeltaTime();
			var disposePool = world.GetPool<DisposableComponent>();
			var blockers = BulletBlocker.Active;

			// --- Build the collider → entity map for all mobs once per frame.
			_mobColliderMap.Clear();
			var mobFilter = world.Filter<MobComponent>().Inc<ColliderComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var col = ref colliderPool.Get(mobEntity);
				if (col.CollisionType == CollisionType.Mob && col.Value != null)
					_mobColliderMap[col.Value] = mobEntity;
			}

			// --- And the map of breakable environment objects (same pattern).
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

				// Level walls / closed gates (BulletBlocker) stop bullets of both teams: test this frame's path.
				if (blockers.Count > 0 && TryHitBlocker(blockers, position - move.Direction * (move.Speed * dt), position,
					    out var wallHit, out var impactEffect))
				{
					disposePool.Get(bulletEntity).IsDisposed = true;
					move.Transform.position = wallHit;
					if (overlapPool.Has(bulletEntity))
						overlapPool.Del(bulletEntity);
					if (!string.IsNullOrEmpty(impactEffect))
					{
						ref var fx = ref world.CreateSimpleEntity<RequestEffectComponent>();
						fx.EffectId = impactEffect;
						fx.Position = wallHit;
					}
					continue;
				}
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
						// For fast bullets: the frame's path + radius.
						float distance = Mathf.Max(bullet.Radius, move.Speed * dt + bullet.Radius);
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
					// Player bullets hit mobs; Enemy bullets don't (no friendly fire on mobs).
					if (!isEnemyBullet && _mobColliderMap.TryGetValue(col, out var mobEntityId))
					{
						// FixedList128Bytes<int>: ~31 elements max — headroom for a single bullet.
						if (mobHits.Length < mobHits.Capacity)
							mobHits.Add(mobEntityId);
						hasAnyMobHit = true;
					}
					else if (_breakableColliderMap.TryGetValue(col, out var breakableEntityId))
					{
						// Breakable environment — cover for bullets of both sides.
						if (breakableHits.Length < breakableHits.Capacity)
							breakableHits.Add(breakableEntityId);
						hasAnyBreakableHit = true;
					}
				}

				// Enemy bullet vs player: the only target. Top-down (counted horizontally: the bullet flies at
				// muzzle height, the player stands at the origin) + swept segment for the frame against tunneling of
				// fast bullets — take the point of the [prev→cur] segment closest to the player.
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

		// Nearest blocker hit along from→to (by distance from 'from').
		private static bool TryHitBlocker(System.Collections.Generic.List<BulletBlocker> blockers, Vector3 from, Vector3 to,
			out Vector3 hit, out string effectId)
		{
			hit = default;
			effectId = null;
			float best = float.MaxValue;
			for (int i = 0; i < blockers.Count; i++)
			{
				var blocker = blockers[i];
				if (blocker == null || !blocker.Intersects(from, to, out var point))
					continue;
				float distSqr = (point - from).sqrMagnitude;
				if (distSqr >= best)
					continue;
				best = distSqr;
				hit = point;
				effectId = blocker.ImpactEffectId;
			}
			return best < float.MaxValue;
		}
	}
}
