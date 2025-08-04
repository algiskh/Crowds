using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class FollowSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var movePool = world.GetPool<MoveComponent>();
			var followPool = world.GetPool<FollowTarget>();
			var offsetPool = world.GetPool<FollowerOffset>();
			var mobPool = world.GetPool<FollowerComponent>();

			var filter = world.Filter<FollowTarget>()
				.Inc<FollowerOffset>()
				.Inc<MoveComponent>()
				.Inc<FollowerComponent>()
				.End();

			foreach (var entity in filter)
			{
				ref var movement = ref movePool.Get(entity);
				ref var target = ref followPool.Get(entity);
				ref var offset = ref offsetPool.Get(entity);
				ref var follower = ref mobPool.Get(entity);

				if (target.Target == null || follower.Value == null)
					continue;

				Vector3 targetPos = target.Target.position + offset.Value;
				Vector3 currentPos = follower.Value.position;
				float distance = Vector3.Distance(currentPos, targetPos);

				// --- Ограничение максимального разрыва ---
				if (target.MatchTargetSpeedIfFar && distance > target.MatchSpeedDistance)
				{
					// Ставим на границу лимита
					Vector3 dir = (currentPos - targetPos).normalized;
					follower.Value.position = targetPos + dir * target.MatchSpeedDistance;

					// После телепорта/догонки пересчитываем расстояние
					currentPos = follower.Value.position;
					distance = Vector3.Distance(currentPos, targetPos);

					// Если ещё есть дистанция для плавного движения — двигаем обычным образом
					if (distance > target.Threshold)
					{
						float speed = movement.Speed;
						if (target.IsAcceleratable)
						{
							speed += target.AccelerationMultiplier * distance;
							if (target.MaxAccelerationMultiplier > 0f)
								speed = Mathf.Min(speed, movement.Speed * target.MaxAccelerationMultiplier);
						}

						float moveStep = speed * Time.deltaTime;
						Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, moveStep);
						follower.Value.position = newPos;
					}
					continue;
				}

				// Обычное движение, если в пределах лимита и нужно ещё идти к цели
				if (distance > target.Threshold)
				{
					float speed = movement.Speed;
					if (target.IsAcceleratable)
					{
						speed += target.AccelerationMultiplier * distance;
						if (target.MaxAccelerationMultiplier > 0f)
							speed = Mathf.Min(speed, movement.Speed * target.MaxAccelerationMultiplier);
					}

					float moveStep = speed * Time.deltaTime;
					Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, moveStep);
					follower.Value.position = newPos;
				}
				// Если расстояние уже маленькое — стоим.
			}
		}
	}
}