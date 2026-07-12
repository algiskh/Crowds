using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Поведение моба-стрелка. Во всём остальном это обычный моб (MobComponent), но вместо
	/// контактного урона он бьёт телеграфированным выстрелом — так же, как моб ближнего боя
	/// (см. MeleeAttackerSystem), только на дистанции:
	///
	///  • Chase    — игрок дальше AttackRange: подходит обычным патфайндингом (систему не трогаем).
	///  • Windup   — игрок в пределах AttackRange: останавливается, целится, проигрывает "attack" и
	///               через RangedMobConfig.WindupDelay (замах) делает выстрел (RequestSpawnBulletComponent,
	///               Team=Enemy).
	///  • Cooldown — после выстрела стоит RangedMobConfig.Cooldown секунд; затем снова Chase.
	///
	/// Выстрел использует тот же путь, что и оружие игрока: RequestSpawnBulletComponent → BulletSystem.
	/// Отличается только Team=Enemy (пуля бьёт игрока, а не мобов — см. BulletOverlapSystem/CollisionSystem)
	/// и точкой спауна (дуло моба). Урон/скорость/радиус/разброс/калибр берутся из вложенного GunConfig.
	///
	/// Запускается после MobPathfindingSystem и до MoveSystem (рядом с MeleeAttackerSystem): в
	/// Windup/Cooldown чистит путь к игроку, тем самым останавливая моба. Контактный урон для таких
	/// мобов отключён в CollisionSystem.
	/// </summary>
	public sealed class RangedAttackerSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			if (!world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton) || playerSingleton.Value == null)
				return;
			Vector3 playerPos = playerSingleton.Value.transform.position;

			world.TryGetAsSingleton<SoundHolderComponent>(out var soundHolder);

			var attackerPool = world.GetPool<RangedAttackerComponent>();
			var mobPool = world.GetPool<MobComponent>();
			var movePool = world.GetPool<MoveComponent>();
			var pathPool = world.GetPool<MovePath>();
			var recalcRequestPool = world.GetPool<PathRecalculationRequest>();
			var animPool = world.GetPool<AnimationStateComponent>();
			var bulletRequestPool = world.GetPool<RequestSpawnBulletComponent>();

			float dt = Time.deltaTime;

			var filter = world.Filter<RangedAttackerComponent>().Inc<MobComponent>().Inc<MoveComponent>().End();
			foreach (var entity in filter)
			{
				ref var attacker = ref attackerPool.Get(entity);
				ref var mob = ref mobPool.Get(entity);

				if (mob.Value == null || !mob.Value.gameObject.activeSelf ||
					attacker.Config == null || attacker.Config.GunConfig == null)
					continue;

				var config = attacker.Config;
				Vector3 mobPos = mob.Value.transform.position;

				Vector3 flat = playerPos - mobPos;
				flat.y = 0f;
				float distance = flat.magnitude;

				switch (attacker.State)
				{
					case RangedAttackerState.Chase:
						if (distance <= config.AttackRange)
							EnterWindup(entity, ref attacker, mob, mobPos, playerPos, animPool);
						// иначе остаёмся в Chase — движение к игроку обеспечивает MobPathfindingSystem/MoveSystem.
						break;

					case RangedAttackerState.Windup:
						StopMovement(entity, movePool, pathPool);
						FacePlayer(mob, mobPos, playerPos);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
						{
							Fire(world, bulletRequestPool, soundHolder, config, mobPos, playerPos);
							EnterCooldown(ref attacker);
						}
						break;

					case RangedAttackerState.Cooldown:
						StopMovement(entity, movePool, pathPool);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
							EnterChase(entity, ref attacker, recalcRequestPool, animPool);
						break;
				}
			}
		}

		#region State transitions

		private void EnterWindup(int entity, ref RangedAttackerComponent attacker, in MobComponent mob,
			Vector3 mobPos, Vector3 playerPos, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = RangedAttackerState.Windup;
			attacker.Timer = attacker.Config.WindupDelay;
			FacePlayer(mob, mobPos, playerPos);
			// Одна анимация "attack" на все три фазы (замах → выстрел → восстановление).
			RequestAnimation(animPool, entity, AnimationType.Attack);
		}

		private void EnterCooldown(ref RangedAttackerComponent attacker)
		{
			attacker.State = RangedAttackerState.Cooldown;
			attacker.Timer = attacker.Config.Cooldown;
			// Анимацию не трогаем — клип "attack" доигрывает фазу восстановления.
		}

		private void EnterChase(int entity, ref RangedAttackerComponent attacker,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = RangedAttackerState.Chase;
			RequestAnimation(animPool, entity, AnimationType.Run);

			// Заставляем патфайндинг проложить путь к игроку сразу же, а не ждать интервала.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		#endregion

		#region Fire

		/// <summary>
		/// Момент выстрела: создаёт RequestSpawnBulletComponent (Team=Enemy) от дула моba по направлению
		/// к игроку. Дальше пулю поднимает BulletSystem — тот же путь, что у оружия игрока. Разброс,
		/// количество снарядов, урон/скорость/радиус/калибр — из вложенного GunConfig.
		/// </summary>
		private void Fire(EcsWorld world, EcsPool<RequestSpawnBulletComponent> bulletRequestPool,
			in SoundHolderComponent soundHolder, RangedMobConfig config, Vector3 mobPos, Vector3 playerPos)
		{
			Vector3 toPlayer = playerPos - mobPos;
			toPlayer.y = 0f;
			Vector3 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;

			// Дуло: приподнято над origin и сдвинуто вперёд, чтобы пуля не спаунилась внутри своего коллайдера.
			Vector3 muzzlePos = mobPos + Vector3.up * config.MuzzleHeight + dir * config.MuzzleForwardOffset;

			var requestEntity = world.NewEntity();
			ref var request = ref bulletRequestPool.Add(requestEntity);
			request.Position = muzzlePos;
			request.Direction = dir;
			request.GunConfig = config.GunConfig;
			request.Team = BulletTeam.Enemy;

			// Звук выстрела (если задан в GunConfig и есть SoundHolder). У мобов нет общего AudioSource —
			// проигрываем позиционно, как ближняя атака мобов (см. MeleeSpawnSystem).
			if (soundHolder.Value != null && !string.IsNullOrEmpty(config.GunConfig.FireSoundId))
			{
				var clip = soundHolder.Value.GetClip(config.GunConfig.FireSoundId);
				if (clip != null)
					AudioSource.PlayClipAtPoint(clip, muzzlePos);
			}
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
