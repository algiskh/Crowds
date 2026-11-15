using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Melee mob behavior. In every other respect it's a regular mob (MobComponent), but instead of
	/// contact damage it strikes with a telegraphed melee attack — just like the player (see MeleeSpawnSystem):
	///
	///  • Chase    — player beyond AttackRange: approaches via regular pathfinding (this system doesn't touch it).
	///  • Windup   — player within AttackRange: stops, plays "attack" and
	///               after MeleeConfig.Delay (windup) deals damage (RequestMeleeComponent).
	///  • Cooldown — after the strike stands for MeleeConfig.Cooldown seconds (the same animation plays
	///               through the recovery phase); then Chase again.
	///
	/// All three phases (windup → strike → recovery) play in a single "attack" animation,
	/// started once on entering Windup. Between individual attacks the mob returns to Chase
	/// (animation "run") for one frame, so the "attack" clip is guaranteed to replay.
	///
	/// Runs after MobPathfindingSystem and before MoveSystem: in Windup/Cooldown it clears the path
	/// built by pathfinding toward the player, thereby stopping the mob. Contact damage is
	/// disabled for such mobs in CollisionSystem — otherwise the damage would double.
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
						// otherwise we stay in Chase — movement toward the player is handled by MobPathfindingSystem/MoveSystem.
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
			// One "attack" animation for all three phases (windup → strike → recovery).
			RequestAnimation(animPool, entity, AnimationType.Attack);
		}

		private void EnterCooldown(ref MeleeAttackerComponent attacker)
		{
			attacker.State = MeleeAttackerState.Cooldown;
			attacker.Timer = attacker.Config.MeleeConfig.Cooldown;
			// Don't touch the animation — the "attack" clip plays through the recovery phase.
		}

		private void EnterChase(int entity, ref MeleeAttackerComponent attacker,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = MeleeAttackerState.Chase;
			RequestAnimation(animPool, entity, AnimationType.Run);

			// Make pathfinding build a path to the player right away, not wait for the interval.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		#endregion

		#region Attack

		/// <summary>
		/// The strike moment: creates a RequestMeleeComponent at the point in front of the mob (toward
		/// the player). Damage/radius/targets/debuffs come from MeleeConfig — the same model as the player's
		/// attack (see MeleeSpawnSystem). Delay=0: hit right away, the windup was already played by the Windup phase.
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
		/// Full stop: kills the direction and clears the path to the player built by pathfinding
		/// (waypoints are cleared, but the path component isn't deleted — the list is reused by pathfinding).
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
