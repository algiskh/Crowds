using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class BulletOverlapSystem : IEcsRunSystem
	{
		private const int BUFFER_SIZE = 32;

		private readonly Collider[] _overlapBuffer = new Collider[BUFFER_SIZE];
		private readonly RaycastHit[] _raycastBuffer = new RaycastHit[BUFFER_SIZE];

		// Переиспользуется каждый кадр: collider → mob entity id.
		private readonly Dictionary<Collider, int> _mobColliderMap = new Dictionary<Collider, int>(64);

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var bulletPool = world.GetPool<BulletComponent>();
			var overlapPool = world.GetPool<BulletOverlapComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();
			var mobPool = world.GetPool<MobComponent>();

			int layerMask = ~0;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				layerMask = mainHolder.Value.MobLayerMask.value;

			// --- Один раз за кадр строим collider → entity карту для всех мобов.
			_mobColliderMap.Clear();
			var mobFilter = world.Filter<MobComponent>().Inc<ColliderComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var col = ref colliderPool.Get(mobEntity);
				if (col.CollisionType == CollisionType.Mob && col.Value != null)
					_mobColliderMap[col.Value] = mobEntity;
			}

			var bulletFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().End();
			foreach (var bulletEntity in bulletFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				ref var move = ref movePool.Get(bulletEntity);
				if (move.Transform == null) continue;

				var position = move.Transform.position;
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
				Unity.Collections.FixedList128Bytes<int> mobHits = default;

				for (int i = 0; i < hitCount; i++)
				{
					var col = _overlapBuffer[i];
					if (col == null) continue;
					if (_mobColliderMap.TryGetValue(col, out var mobEntityId))
					{
						// FixedList128Bytes<int>: ~31 элементов max — с запасом для одной пули.
						if (mobHits.Length < mobHits.Capacity)
							mobHits.Add(mobEntityId);
						hasAnyMobHit = true;
					}
				}

				if (hasAnyMobHit)
				{
					ref var overlap = ref overlapPool.Has(bulletEntity)
						? ref overlapPool.Get(bulletEntity)
						: ref overlapPool.Add(bulletEntity);
					overlap.MobHits = mobHits;
				}
				else if (overlapPool.Has(bulletEntity))
				{
					overlapPool.Del(bulletEntity);
				}
			}
		}
	}
}
