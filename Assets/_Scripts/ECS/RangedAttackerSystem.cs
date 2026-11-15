using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Ranged mob behavior. In every other respect it's a regular mob (MobComponent), but instead of
	/// contact damage it strikes with a telegraphed shot — just like the melee mob
	/// (see MeleeAttackerSystem), only at range:
	///
	///  • Chase    — player beyond AttackRange: approaches via regular pathfinding (this system doesn't touch it).
	///  • Windup   — player within AttackRange: stops, aims, plays WindupAnimation and
	///               after RangedMobConfig.WindupDelay (windup) fires (RequestSpawnBulletComponent,
	///               Team=Enemy) and switches to FireAnimation.
	///  • Burst    — only if BurstCount > 1: the remaining shots, BurstInterval apart, still tracking the player
	///               (FireAnimation restarted per shot if RestartFireAnimationPerShot).
	///  • Cooldown — after the last shot stands for RangedMobConfig.Cooldown seconds; then aims again if the
	///               player is still in range, otherwise Chase.
	///
	/// The shot uses the same path as the player's weapon: RequestSpawnBulletComponent → BulletSystem.
	/// Only Team=Enemy differs (the bullet hits the player, not mobs — see BulletOverlapSystem/CollisionSystem)
	/// and the spawn point (the mob's muzzle). Damage/speed/radius/spread/caliber come from the embedded GunConfig.
	///
	/// Runs after MobPathfindingSystem and before MoveSystem (next to MeleeAttackerSystem): in
	/// Windup/Cooldown it clears the path to the player, thereby stopping the mob. Contact damage is
	/// disabled for such mobs in CollisionSystem.
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
						// otherwise we stay in Chase — movement toward the player is handled by MobPathfindingSystem/MoveSystem.
						break;

					case RangedAttackerState.Windup:
						StopMovement(entity, movePool, pathPool);
						FacePlayer(mob, mobPos, playerPos);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
						{
							// First shot of the attack: switch to the fire clip.
							Fire(world, bulletRequestPool, soundHolder, config, mob.Value, mobPos, playerPos);
							RequestAnimation(animPool, entity, config.FireAnimation);
							attacker.ShotsLeft = config.BurstCount - 1;
							if (attacker.ShotsLeft > 0)
							{
								attacker.State = RangedAttackerState.Burst;
								attacker.Timer = config.BurstInterval;
							}
							else
								EnterCooldown(ref attacker);
						}
						break;

					case RangedAttackerState.Burst:
						// Rest of the burst: stay put, keep tracking the player, fire every BurstInterval.
						StopMovement(entity, movePool, pathPool);
						FacePlayer(mob, mobPos, playerPos);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
						{
							Fire(world, bulletRequestPool, soundHolder, config, mob.Value, mobPos, playerPos);
							if (config.RestartFireAnimationPerShot)
								RequestAnimation(animPool, entity, config.FireAnimation, restart: true);
							attacker.ShotsLeft--;
							if (attacker.ShotsLeft > 0)
								attacker.Timer += config.BurstInterval;
							else
								EnterCooldown(ref attacker);
						}
						break;

					case RangedAttackerState.Cooldown:
						StopMovement(entity, movePool, pathPool);
						attacker.Timer -= dt;
						if (attacker.Timer <= 0f)
						{
							// Still in range → aim again right away (no one-frame flick into the chase clip).
							if (distance <= config.AttackRange)
								EnterWindup(entity, ref attacker, mob, mobPos, playerPos, animPool);
							else
								EnterChase(entity, ref attacker, recalcRequestPool, animPool);
						}
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
			// Windup clip (default "attack" for all three phases; a shooter with aim/shoot clips sets them in the config).
			RequestAnimation(animPool, entity, attacker.Config.WindupAnimation);
		}

		private void EnterCooldown(ref RangedAttackerComponent attacker)
		{
			attacker.State = RangedAttackerState.Cooldown;
			attacker.Timer = attacker.Config.Cooldown;
			attacker.ShotsLeft = 0;
			// The fire clip (requested on the first shot) plays through the recovery phase.
		}

		private void EnterChase(int entity, ref RangedAttackerComponent attacker,
			EcsPool<PathRecalculationRequest> recalcRequestPool, EcsPool<AnimationStateComponent> animPool)
		{
			attacker.State = RangedAttackerState.Chase;
			RequestAnimation(animPool, entity, attacker.Config.ChaseAnimation);

			// Make pathfinding build a path to the player right away, not wait for the interval.
			if (!recalcRequestPool.Has(entity))
				recalcRequestPool.Add(entity);
		}

		#endregion

		#region Fire

		/// <summary>
		/// The shot moment: creates a RequestSpawnBulletComponent (Team=Enemy) from the muzzle toward
		/// the player. BulletSystem picks the bullet up from here — same path as the player's weapon. Spread,
		/// projectile count, damage/speed/radius/caliber — from the embedded GunConfig.
		/// Spawn point = the held weapon's muzzle (<see cref="Mob.Weapon"/>) if the prefab has one, otherwise the
		/// config offsets. The direction is always flat toward the player (the animated muzzle only gives the origin).
		/// </summary>
		private void Fire(EcsWorld world, EcsPool<RequestSpawnBulletComponent> bulletRequestPool,
			in SoundHolderComponent soundHolder, RangedMobConfig config, Mob mob, Vector3 mobPos, Vector3 playerPos)
		{
			var weapon = mob.Weapon;
			Transform muzzle = weapon != null ? weapon.Muzzle : null;

			Vector3 origin = muzzle != null ? muzzle.position : mobPos;
			Vector3 toPlayer = playerPos - origin;
			toPlayer.y = 0f;
			Vector3 dir = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : mob.transform.forward;

			// Fallback muzzle: raised above the origin and shifted forward so the bullet doesn't spawn inside its collider.
			Vector3 muzzlePos = muzzle != null
				? muzzle.position
				: mobPos + Vector3.up * config.MuzzleHeight + dir * config.MuzzleForwardOffset;

			// Muzzle flash: a pooled effect from EffectsHolder (follows the muzzle when its FxWrapper is IsChild).
			if (muzzle != null && !string.IsNullOrEmpty(weapon.MuzzleEffectId))
			{
				ref var fx = ref world.CreateSimpleEntity<RequestEffectComponent>();
				fx.EffectId = weapon.MuzzleEffectId;
				fx.Position = muzzlePos;
				fx.Parent = muzzle;
				fx.Rotation = Quaternion.LookRotation(dir).eulerAngles.y;
			}

			var requestEntity = world.NewEntity();
			ref var request = ref bulletRequestPool.Add(requestEntity);
			request.Position = muzzlePos;
			request.Direction = dir;
			request.GunConfig = config.GunConfig;
			request.Team = BulletTeam.Enemy;

			// Shot sound (if set in GunConfig and a SoundHolder exists). Mobs have no shared AudioSource —
			// play it positionally, like mob melee attacks (see MeleeSpawnSystem).
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
		private void RequestAnimation(EcsPool<AnimationStateComponent> animPool, int entity, AnimationType type, bool restart = false)
		{
			ref var anim = ref animPool.Has(entity) ? ref animPool.Get(entity) : ref animPool.Add(entity);
			anim.Requested = type;
			if (restart)
				anim.Restart = true;
		}

		#endregion
	}
}
