using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Поведение моба ближнего боя. Во всём остальном это обычный моб (MobComponent), но вместо
	/// контактного урона он бьёт телеграфированной ближней атакой — так же, как игрок (см. MeleeSpawnSystem):
	///
	///  • Chase    — игрок дальше AttackRange: подходит обычным патфайндингом (систему не трогаем).
	///  • Windup   — игрок в пределах AttackRange: останавливается, проигрывает "attack" и
	///               через MeleeConfig.Delay (замах) наносит урон (RequestMeleeComponent).
	///  • Cooldown — после удара стоит MeleeConfig.Cooldown секунд (та же анимация доигрывает фазу
	///               восстановления); затем снова Chase.
	///
	/// Все три фазы (замах → удар → восстановление) проигрываются одной анимацией "attack",
	/// запускаемой один раз при входе в Windup. Между отдельными атаками моб на кадр возвращается
	/// в Chase (анимация "run"), чтобы клип "attack" гарантированно переигрался заново.
	///
	/// Запускается после MobPathfindingSystem и до MoveSystem: в Windup/Cooldown чистит путь,
	/// проложенный патфайндингом к игроку, тем самым останавливая моба. Контактный урон для таких
	/// мобов отключён в CollisionSystem — иначе урон удвоился бы.
	/// </summary>
	public sealed class MeleeAttackerSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				return;
			Vector3 playerPos = playerSingleton.Value.transform.position;

			var attackerPool = world.GetPool<MeleeAttackerComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();
			var recalcRequestPool = world.GetPool<PathRecalculationRequest>();
			var animPool = world.GetPool<AnimationStateComponent>();
			var meleeRequestPool = world.GetPool<RequestMeleeComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<MeleeAttackerComponent>().Inc<MobComponent>().Inc<MoveComponent>().End();
			foreach (var entity in filter)
			{
				ref var attacker = ref attackerPool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf ||
					attacker.Config == null || attacker.Config.MeleeConfig == null)
					continue;

				var meleeConfig = attacker.Config.MeleeConfig;
				Vector3 mobPos = mob.Value.transform.position;

				Vector3 flat = playerPos - mobPos;
				flat.y = 0f;
				float distance = flat.magnitude;

				switch (attacker.State)
				{
					case MeleeAttackerState.Chase:
						if (distance <= attacker.Config.AttackRange)
							EnterWindup(entity, ref attacker, mob, mobPos, playerPos, animPool);
						// иначе остаёмся в Chase — движение к игроку обеспечивает MobPathfindingSystem/MoveSystem.
						break;

					case MeleeAttackerState.Windup:
						StopMovement(entity, movePool, pathPool);
						FacePlayer(mob, mobPos, playerPos);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
						{
							Strike(world, meleeRequestPool, entity, meleeConfig, mob, mobPos, playerPos);
							EnterCooldown(ref attacker);
						}
						break;

					case MeleeAttackerState.Cooldown:
						StopMovement(entity, movePool, pathPool);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
							EnterChase(entity, ref attacker, recalcRequestPool, animPool);
						break;
				}
			}
		}

		#region State transitions

		private void EnterWindup(int entity, ref MeleeAttackerComponent attacker, in MobComponent mob,
			Vector3 mobPos, Vector3 playerPos, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = MeleeAttackerState.Windup;
			attacker.Timer = attacker.Config.MeleeConfig.Delay;
			FacePlayer(mob, mobPos, playerPos);
			// Одна анимация "attack" на все три фазы (замах → удар → восстановление).
			RequestAnimation(animPool, entity, AnimationType.Attack);
		}

		private void EnterCooldown(ref MeleeAttackerComponent attacker)
		{
			attacker.State = MeleeAttackerState.Cooldown;
			attacker.Timer = attacker.Config.MeleeConfig.Cooldown;
			// Анимацию не трогаем — клип "attack" доигрывает фазу восстановления.
		}

		private void EnterChase(int entity, ref MeleeAttackerComponent attacker,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = MeleeAttackerState.Chase;
			RequestAnimation(animPool, entity, AnimationType.Run);

			// Заставляем патфайндинг проложить путь к игроку сразу же, а не ждать интервала.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		#endregion

		#region Attack

		/// <summary>
		/// Момент удара: создаёт RequestMeleeComponent в точке перед мобом (по направлению к игроку).
		/// Урон/радиус/цель/дебаффы берутся из MeleeConfig — та же модель, что у атаки игрока
		/// (см. MeleeSpawnSystem). Delay=0: бьём сразу, замах уже отыгран фазой Windup.
		/// </summary>
		private void Strike(EcsWorld world, EcsPool<RequestMeleeComponent> meleeRequestPool, int entity,
			MeleeConfig meleeConfig, in MobComponent mob, Vector3 mobPos, Vector3 playerPos)
		{
			Vector3 toPlayer = playerPos - mobPos;
			toPlayer.y = 0f;
			Vector3 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : mob.Value.transform.forward;

			var requestEntity = world.NewEntity();
			ref var request = ref meleeRequestPool.Add(requestEntity);
			request.SourceEntity = entity;
			request.Config = meleeConfig;
			request.Position = mobPos + dir * meleeConfig.Range;
			request.Rotation = mob.Value.transform.eulerAngles.y;
			request.Delay = 0f;
		}

		#endregion

		#region Movement / view helpers

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
