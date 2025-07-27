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

				// Если уже достаточно близко — стоим
				if (distance < target.Threshold)
					continue;

				// === Динамическое ускорение ===
				float speed = movement.Speed;
				if (target.IsAcceleratable)
				{
					// Базовое ускорение — линейно от расстояния
					speed += target.AccelerationMultiplier * distance;

					// Если задан максимальный множитель, ограничиваем максимальную скорость
					if (target.MaxAccelerationMultiplier > 0f)
						speed = Mathf.Min(speed, movement.Speed * target.MaxAccelerationMultiplier);
				}

				float moveStep = speed * Time.deltaTime;
				Vector3 newPos = Vector3.MoveTowards(currentPos, targetPos, moveStep);

				follower.Value.position = newPos;

				// --- по желанию: если хочешь поворот за движением ---
				// Vector3 moveDir = targetPos - currentPos;
				// if (moveDir.sqrMagnitude > 0.0001f)
				//     follower.Value.right = moveDir.normalized;
			}
		}
	}
}