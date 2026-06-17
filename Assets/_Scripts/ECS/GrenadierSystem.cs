using Leopotam.EcsLite;
using Scene.Animation;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	/// <summary>
	/// Поведение моба-гренадёра. Во всём остальном это обычный моб (MobComponent), но вместо
	/// того чтобы лезть в ближний бой, он держит дистанцию и кидает гранаты:
	///
	///  • Chase    — игрок дальше X: подходит ближе обычным патфайндингом (систему не трогаем).
	///  • Throw    — игрок в полосе [Y, X]: останавливается, проигрывает "throw", по окончании
	///               замаха (ThrowWindup) бросает гранату в текущую позицию игрока.
	///  • Cooldown — после броска стоит ThrowCooldown секунд (анимация "throw_cooldown").
	///  • Flee     — игрок ближе Y: отходит на свободную точку NavMesh подальше от игрока.
	///
	/// Запускается после MobPathfindingSystem и до MoveSystem: в нестандартных состояниях
	/// перетирает/чистит путь, проложенный патфайндингом к игроку, тем самым управляя движением.
	/// Граната моба задевает взрывом игрока (а не мобов) — см. GrenadeLauncher/ExplosionSystem.
	/// </summary>
	public sealed class GrenadierSystem : IEcsRunSystem
	{
		// Углы (град) для поиска свободной точки отхода: сначала строго от игрока, затем в стороны.
		private static readonly float[] _fleeAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 135f, -135f, 180f };

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				return;
			Vector3 playerPos = playerSingleton.Value.transform.position;

			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;

			var grenadierPool = world.GetPool<GrenadierComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();
			var recalcRequestPool = world.GetPool<PathRecalculationRequest>();
			var animPool = world.GetPool<AnimationStateComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<GrenadierComponent>().Inc<MobComponent>().Inc<MoveComponent>().End();
			foreach (var entity in filter)
			{
				ref var grenadier = ref grenadierPool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf || grenadier.Config == null)
					continue;

				var config = grenadier.Config;
				Vector3 mobPos = mob.Value.transform.position;

				Vector3 flat = playerPos - mobPos;
				flat.y = 0f;
				float distance = flat.magnitude;

				switch (grenadier.State)
				{
					case GrenadierState.Chase:
						if (distance < config.ThrowMinDistance)
							EnterFlee(entity, ref grenadier, animPool);
						else if (distance <= config.ThrowMaxDistance)
							EnterThrow(entity, ref grenadier, mob, mobPos, playerPos, animPool);
						// иначе остаёмся в Chase — движение к игроку обеспечивает MobPathfindingSystem/MoveSystem.
						break;

					case GrenadierState.Throw:
						StopMovement(entity, movePool, pathPool);
						grenadier.Timer -= dt;
						if (grenadier.Timer <= 0f)
						{
							GrenadeLauncher.Launch(world, mainHolder, config.GrenadeConfig,
								mobPos, playerPos);
							EnterCooldown(entity, ref grenadier, animPool);
						}
						break;

					case GrenadierState.Cooldown:
						StopMovement(entity, movePool, pathPool);
						grenadier.Timer -= dt;
						if (grenadier.Timer <= 0f)
							EnterChase(entity, ref grenadier, recalcRequestPool, animPool);
						break;

					case GrenadierState.Flee:
						if (distance >= config.ThrowMinDistance)
						{
							// Отошёл достаточно далеко — снова кидаем (если в полосе) либо догоняем.
							if (distance <= config.ThrowMaxDistance)
								EnterThrow(entity, ref grenadier, mob, mobPos, playerPos, animPool);
							else
								EnterChase(entity, ref grenadier, recalcRequestPool, animPool);
						}
						else
						{
							FleeStep(entity, ref grenadier, mobPos, playerPos, config.ThrowMinDistance, movePool, pathPool);
						}
						break;
				}
			}
		}

		#region State transitions

		private void EnterChase(int entity, ref GrenadierComponent grenadier,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Chase;
			grenadier.HasFleeTarget = false;
			RequestAnimation(animPool, entity, AnimationType.Run);

			// Заставляем патфайндинг проложить путь к игроку сразу же, а не ждать интервала.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		private void EnterThrow(int entity, ref GrenadierComponent grenadier, in MobComponent mob,
			Vector3 mobPos, Vector3 playerPos, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Throw;
			grenadier.Timer = grenadier.Config.ThrowWindup;
			grenadier.HasFleeTarget = false;
			FacePlayer(mob, mobPos, playerPos);
			RequestAnimation(animPool, entity, AnimationType.Throw);
		}

		private void EnterCooldown(int entity, ref GrenadierComponent grenadier, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Cooldown;
			grenadier.Timer = grenadier.Config.ThrowCooldown;
			RequestAnimation(animPool, entity, AnimationType.ThrowCooldown);
		}

		private void EnterFlee(int entity, ref GrenadierComponent grenadier, EcsPool<AnimationStateComponent> animPool)
		{
			grenadier.State = GrenadierState.Flee;
			grenadier.HasFleeTarget = false;
			RequestAnimation(animPool, entity, AnimationType.Run);
		}

		#endregion

		#region Movement helpers

		/// <summary>
		/// Полная остановка: гасит направление и обнуляет путь к игроку, проложенный патфайндингом
		/// (waypoints чистим, но компонент пути не удаляем — список переиспользуется патфайндингом).
		/// </summary>
		private void StopMovement(int entity, EcsPool<MoveComponent> movePool, EcsPool<MovePath> pathPool)
		{
			ref var move = ref movePool.Get(entity);
			move.Direction = Vector3.zero;

			if (pathPool.Has(entity))
			{
				ref var path = ref pathPool.Get(entity);
				path.Waypoints?.Clear();
			}
		}

		/// <summary>
		/// Шаг отхода: выбирает (при необходимости) свободную точку NavMesh подальше от игрока и
		/// каждый кадр прописывает её единственным waypoint'ом, перетирая путь к игроку.
		/// </summary>
		private void FleeStep(int entity, ref GrenadierComponent grenadier, Vector3 mobPos, Vector3 playerPos,
			float minDistance, EcsPool<MoveComponent> movePool, EcsPool<MovePath> pathPool)
		{
			bool needNew = !grenadier.HasFleeTarget;
			if (!needNew)
			{
				Vector3 toTarget = grenadier.FleeTarget - mobPos;
				toTarget.y = 0f;
				if (toTarget.sqrMagnitude < 0.4f * 0.4f)
					needNew = true; // дошёл до выбранной точки, а игрок всё ещё близко — ищем новую.
			}

			if (needNew && TryFindFleeTarget(mobPos, playerPos, minDistance, out var target))
			{
				grenadier.FleeTarget = target;
				grenadier.HasFleeTarget = true;
			}

			if (!grenadier.HasFleeTarget)
			{
				// Свободного места не нашли — просто стоим, чтобы не лезть в игрока.
				StopMovement(entity, movePool, pathPool);
				return;
			}

			ref var path = ref pathPool.Has(entity) ? ref pathPool.Get(entity) : ref pathPool.Add(entity);
			if (path.Waypoints == null)
				path.Waypoints = new List<Vector3>(2);
			path.Waypoints.Clear();
			path.Waypoints.Add(grenadier.FleeTarget);
			path.CurrentIndex = 0;
		}

		/// <summary>
		/// Ищет валидную точку NavMesh на дистанции > minDistance от игрока, отходя от него;
		/// при заблокированном направлении перебирает углы в стороны ("свободное место рядом").
		/// </summary>
		private bool TryFindFleeTarget(Vector3 mobPos, Vector3 playerPos, float minDistance, out Vector3 result)
		{
			Vector3 away = mobPos - playerPos;
			away.y = 0f;
			if (away.sqrMagnitude < 0.0001f)
				away = Vector3.forward;
			away.Normalize();

			float desired = minDistance + 1.5f;
			for (int i = 0; i < _fleeAngles.Length; i++)
			{
				Vector3 dir = Quaternion.Euler(0f, _fleeAngles[i], 0f) * away;
				Vector3 candidate = playerPos + dir * desired;
				if (NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas))
				{
					result = hit.position;
					return true;
				}
			}

			result = mobPos;
			return false;
		}

		private void FacePlayer(in MobComponent mob, Vector3 mobPos, Vector3 playerPos)
		{
			Vector3 dir = playerPos - mobPos;
			dir.y = 0f;
			if (dir.sqrMagnitude > 0.0001f)
				mob.Value.transform.rotation = Quaternion.LookRotation(dir.normalized);
		}

		// Sets the desired animation on the mob's state component; AnimationSystem applies it to the view.
		private void RequestAnimation(EcsPool<AnimationStateComponent> animPool, int entity, AnimationType type)
		{
			ref var anim = ref animPool.Has(entity) ? ref animPool.Get(entity) : ref animPool.Add(entity);
			anim.Requested = type;
		}

		#endregion
	}
}
