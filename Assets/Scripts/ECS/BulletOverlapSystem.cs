using UnityEngine;
using Leopotam.EcsLite;

namespace ECS
{
	public class BulletOverlapSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var bulletPool = world.GetPool<BulletComponent>();
			var overlapPool = world.GetPool<BulletOverlapComponent>();
			var movePool = world.GetPool<MoveComponent>();

			var bulletFilter = world.Filter<BulletComponent>().Inc<MoveComponent>().End();

			foreach (var bulletEntity in bulletFilter)
			{
				ref var bullet = ref bulletPool.Get(bulletEntity);
				ref var move = ref movePool.Get(bulletEntity);

				var position = move.Transform.position;
				Collider[] overlapped = null;

				switch (bullet.CheckType)
				{
					case BulletCheckType.OverlapSphere:
						overlapped = Physics.OverlapSphere(position, bullet.Radius);
						break;

					case BulletCheckType.Raycast:
						// ћожно хранить в BulletComponent предыдущее положение (prevPosition),
						// либо использовать позицию муза и скорость пули
						var direction = move.Transform.forward;
						var distance = bullet.Radius; // ћожно использовать скорость * Time.deltaTime дл€ fast-moving
						var hits = Physics.RaycastAll(position, direction, distance);

						if (hits.Length > 0)
						{
							overlapped = new Collider[hits.Length];
							for (int i = 0; i < hits.Length; i++)
								overlapped[i] = hits[i].collider;
						}
						else
						{
							overlapped = new Collider[0];
						}
						break;

						// «десь могут быть другие режимы...
				}

				if (overlapped != null && overlapped.Length > 0)
				{
					if (overlapPool.Has(bulletEntity))
					{
						ref var overlap = ref overlapPool.Get(bulletEntity);
						overlap.colliders = overlapped;
					}
					else
					{
						ref var overlap = ref overlapPool.Add(bulletEntity);
						overlap.colliders = overlapped;
					}
				}
				else
				{
					if (overlapPool.Has(bulletEntity))
						overlapPool.Del(bulletEntity);
				}
			}
		}
	}
}