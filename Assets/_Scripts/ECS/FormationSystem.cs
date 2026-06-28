using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Ведёт ведомых мобов в строю (§6/§7c инструкции, минимальная версия без navmesh).
	/// Каждый кадр: мировой слот = leader.position + leader.rotation * SlotOffset
	/// (поворот ведущего уже сглажен MoveSystem, поэтому отдельный formationDir не считаем).
	/// Ведомый рулит к слоту через MoveComponent.Direction; MoveDirect двигает на
	/// Speed * |Direction|, поэтому скорость регулируем длиной Direction (ближе к слоту — медленнее,
	/// далеко — догоняет). При гибели ведущего (Unpack == false) ведомый возвращается к обычной
	/// погоне за игроком (Promote). Должна стоять после MobPathfindingSystem и до MoveSystem.
	/// </summary>
	public sealed class FormationSystem : IEcsRunSystem
	{
		private const float FormationTime = 0.5f; // §7c: целевая скорость = dist / FormationTime
		private const float Precision = 1.0f;     // §7c базовый допуск попадания в слот (м)
		private const float MinSpeedFrac = 0.15f; // не ползти медленнее этой доли базовой скорости
		private const float MaxSpeedFrac = 1.6f;  // дать догонять быстрее базовой скорости

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused)
				return;

			var followerPool = world.GetPool<FormationFollowerComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var mobPool = world.GetPool<MobComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<FormationFollowerComponent>()
				.Inc<MoveComponent>()
				.Inc<MobComponent>()
				.End();

			foreach (var entity in filter)
			{
				ref var follower = ref followerPool.Get(entity);
				ref var move = ref movePool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf)
					continue;

				// Ведущий жив? EcsPackedEntity отлавливает и гибель, и переиспользование из пула.
				if (!follower.Leader.Unpack(world, out int leaderEntity) || !mobPool.Has(leaderEntity))
				{
					Promote(world, entity, followerPool);
					continue;
				}

				ref var leaderMob = ref mobPool.Get(leaderEntity);
				if (leaderMob.Value == null || !leaderMob.Value.gameObject.activeSelf)
				{
					Promote(world, entity, followerPool);
					continue;
				}

				Transform leaderTransform = leaderMob.Value.transform;
				Transform selfTransform = mob.Value.transform;

				// §6: мировой слот относительно позы ведущего.
				Vector3 worldSlot = leaderTransform.position + leaderTransform.rotation * follower.SlotOffset;
				Vector3 self = selfTransform.position;
				worldSlot.y = self.y;

				Vector3 toSlot = worldSlot - self;
				toSlot.y = 0f;
				float dist = toSlot.magnitude;

				// §7c гистерезис: в строю держаться легче, до строя — попасть труднее.
				float tol = follower.InFormation ? Precision * 2f : Precision * 0.5f;

				if (dist <= tol)
				{
					follower.InFormation = true;
					move.Direction = Vector3.zero; // в слоте — стоим
					// Доводим лицо под ведущего, чтобы отряд смотрел согласованно.
					selfTransform.rotation = Quaternion.Slerp(
						selfTransform.rotation, leaderTransform.rotation, move.Speed * dt);
				}
				else
				{
					follower.InFormation = false;
					Vector3 dir = toSlot / dist;

					// §7c пропорциональный регулятор: целевая скорость = dist / FormationTime,
					// в долях базовой Speed (MoveDirect домножит Speed на |Direction|).
					float wanted = dist / FormationTime;
					float frac = Mathf.Clamp(wanted / Mathf.Max(move.Speed, 0.01f), MinSpeedFrac, MaxSpeedFrac);
					move.Direction = dir * frac;

					selfTransform.rotation = Quaternion.Slerp(
						selfTransform.rotation, Quaternion.LookRotation(dir), move.Speed * dt);
				}
			}
		}

		/// <summary>
		/// Ведущий потерян: снимаем со строя и возвращаем моба к обычной погоне за игроком —
		/// добавляем PathRecalculation + внеочередной запрос пересчёта пути.
		/// </summary>
		private static void Promote(EcsWorld world, int entity, EcsPool<FormationFollowerComponent> followerPool)
		{
			followerPool.Del(entity);

			var recalcPool = world.GetPool<PathRecalculation>();
			if (!recalcPool.Has(entity))
			{
				ref var recalc = ref recalcPool.Add(entity);
				recalc.Interval = world.GetAsSingleton<MainHolderComponent>().Value.PathRecalculationInterval;
				recalc.LastTime = 0f;
			}

			var requestPool = world.GetPool<PathRecalculationRequest>();
			if (!requestPool.Has(entity))
				requestPool.Add(entity);
		}
	}
}
