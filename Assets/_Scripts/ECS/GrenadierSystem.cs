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
		// Не дальше ±90°: точка всегда остаётся «на стороне моба» и отход не ведёт через игрока.
		private static readonly float[] _fleeAngles = { 0f, 30f, -30f, 60f, -60f, 90f, -90f };
		// Допуск: путь отхода может подойти к игроку не более чем на столько ближе текущей дистанции.
		private const float FleeClearanceTolerance = 0.5f;
		private const float FleeArriveDistance = 0.4f;

		// Переиспользуемые буферы для расчёта пути отхода по NavMesh.
		private readonly NavMeshPath _fleePath = new NavMeshPath();
		private Vector3[] _fleeCorners = new Vector3[16];
		private int _fleeCornerCount;

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
		/// Шаг отхода: выбирает (при необходимости) свободную точку NavMesh подальше от игрока и ведёт
		/// моба к ней по пути NavMesh (углы пути — waypoint'ы). Патфайндинг периодически перетирает
		/// MovePath путём к игроку — тогда путь отхода прокладывается заново от текущей позиции.
		/// </summary>
		private void FleeStep(int entity, ref GrenadierComponent grenadier, Vector3 mobPos, Vector3 playerPos,
			float minDistance, EcsPool<MoveComponent> movePool, EcsPool<MovePath> pathPool)
		{
			bool needNew = !grenadier.HasFleeTarget;
			if (!needNew)
			{
				Vector3 toTarget = grenadier.FleeTarget - mobPos;
				toTarget.y = 0f;
				if (toTarget.sqrMagnitude < FleeArriveDistance * FleeArriveDistance)
					needNew = true; // дошёл до выбранной точки, а игрок всё ещё близко — ищем новую.
			}

			bool hasFreshPath = false;
			if (needNew)
			{
				grenadier.HasFleeTarget = TryFindFleeTarget(mobPos, playerPos, minDistance, out var target);
				if (grenadier.HasFleeTarget)
				{
					grenadier.FleeTarget = target;
					hasFreshPath = true; // TryFindFleeTarget оставил проверенный путь в _fleeCorners.
				}
			}

			if (!grenadier.HasFleeTarget)
			{
				// Свободного места не нашли — просто стоим, чтобы не лезть в игрока.
				StopMovement(entity, movePool, pathPool);
				return;
			}

			// Путь уже ведёт к точке отхода и MoveSystem по нему идёт — не трогаем (иначе сбросили бы прогресс).
			if (!hasFreshPath && IsFollowingFleePath(entity, grenadier.FleeTarget, pathPool))
				return;

			if (!hasFreshPath && !TryCalculateFleePath(mobPos, grenadier.FleeTarget, playerPos))
			{
				// Прежний маршрут больше не годится (перекрыт или ведёт к игроку) — выберем точку заново в след. кадре.
				grenadier.HasFleeTarget = false;
				StopMovement(entity, movePool, pathPool);
				return;
			}

			ref var path = ref pathPool.Has(entity) ? ref pathPool.Get(entity) : ref pathPool.Add(entity);
			if (path.Waypoints == null)
				path.Waypoints = new List<Vector3>(_fleeCorners.Length);
			path.Waypoints.Clear();
			for (int i = 0; i < _fleeCornerCount; i++)
				path.Waypoints.Add(_fleeCorners[i]);
			path.CurrentIndex = 0;
		}

		private static bool IsFollowingFleePath(int entity, Vector3 fleeTarget, EcsPool<MovePath> pathPool)
		{
			if (!pathPool.Has(entity))
				return false;
			ref var path = ref pathPool.Get(entity);
			if (path.Waypoints == null || path.Waypoints.Count == 0)
				return false;
			Vector3 last = path.Waypoints[path.Waypoints.Count - 1];
			last.y = fleeTarget.y;
			return (last - fleeTarget).sqrMagnitude < 0.01f;
		}

		/// <summary>
		/// Ищет валидную точку NavMesh на дистанции >= minDistance от игрока, отходя от него;
		/// при заблокированном направлении перебирает углы в стороны (в пределах ±90° от «прочь от игрока»).
		/// Точка принимается только если до неё есть полный путь NavMesh, не подводящий моба к игроку.
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
				if (!NavMesh.SamplePosition(candidate, out var hit, 2f, NavMesh.AllAreas))
					continue;

				Vector3 flat = hit.position - playerPos;
				flat.y = 0f;
				if (flat.sqrMagnitude < minDistance * minDistance)
					continue;

				if (TryCalculateFleePath(mobPos, hit.position, playerPos))
				{
					result = hit.position;
					return true;
				}
			}

			result = mobPos;
			return false;
		}

		/// <summary>
		/// Считает путь NavMesh до точки отхода в _fleeCorners. Путь отвергается, если он неполный или
		/// хоть один его отрезок проходит к игроку ближе, чем моб стоит сейчас (минус допуск).
		/// </summary>
		private bool TryCalculateFleePath(Vector3 from, Vector3 to, Vector3 playerPos)
		{
			_fleeCornerCount = 0;
			if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, _fleePath)
				|| _fleePath.status != NavMeshPathStatus.PathComplete)
				return false;

			int cornerCount = _fleePath.GetCornersNonAlloc(_fleeCorners);
			if (cornerCount == _fleeCorners.Length)
			{
				_fleeCorners = new Vector3[_fleeCorners.Length * 2];
				cornerCount = _fleePath.GetCornersNonAlloc(_fleeCorners);
			}
			if (cornerCount == 0)
				return false;

			Vector2 player = new Vector2(playerPos.x, playerPos.z);
			float clearance = Mathf.Max(0f, Vector2.Distance(new Vector2(from.x, from.z), player) - FleeClearanceTolerance);
			for (int i = 0; i < cornerCount - 1; i++)
			{
				Vector2 a = new Vector2(_fleeCorners[i].x, _fleeCorners[i].z);
				Vector2 b = new Vector2(_fleeCorners[i + 1].x, _fleeCorners[i + 1].z);
				if (DistanceToSegment(player, a, b) < clearance)
					return false;
			}

			_fleeCornerCount = cornerCount;
			return true;
		}

		private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
		{
			Vector2 ab = b - a;
			float lenSq = ab.sqrMagnitude;
			float t = lenSq > 0.0001f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / lenSq) : 0f;
			return Vector2.Distance(p, a + ab * t);
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
