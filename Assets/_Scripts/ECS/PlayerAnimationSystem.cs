using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Drives the player's <see cref="PlayerAnimator"/> from ECS state (read-only: it changes no gameplay data).
	/// Runs after PlayerMovementSystem, so this frame's input, shots (WeaponFireSystem), reload state,
	/// grenade throw (GrenadeThrowSystem) and damage (DamageSystem) are all already applied.
	///
	///   movement  PlayerInputComponent.Move (world) → player-local → MoveX/MoveY + MoveSpeed
	///   weapon    WeaponComponent.GunConfig.AnimationSet → WeaponSet (Rifle/Pistol)
	///   firing    WeaponComponent.IsFiring → IsFiring;  ShotCounter changed → Fire trigger
	///   reload    ReloadingComponent.ReloadTime > 0 → IsReloading (+ ReloadSpeed from GunConfig.ReloadTime)
	///   melee     PlayerInputComponent.IsMeleeing (one frame) → Melee trigger
	///   grenade   GrenadeStateComponent.IsCharging → IsChargingThrow;  ThrowCounter changed → Throw trigger
	///   hit       player health went down (and still alive) → Hit trigger
	///   death     health ≤ 0 or the fail sequence started → IsDead (keeps playing through the pause)
	///   pause     PauseStateComponent.IsPaused → Animator.speed 0
	/// No player model / no PlayerAnimator assigned → the system does nothing.
	/// </summary>
	public sealed class PlayerAnimationSystem : IEcsRunSystem
	{
		private bool _initialized;
		private int _lastShotCounter;
		private int _lastThrowCounter;
		private float _lastHealth;
		private float _hitCooldown;

		// Min interval between hit flinches: poison ticks would otherwise retrigger it constantly.
		private const float HitReactionInterval = 0.5f;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			if (!world.TryGetAsSingleton<PlayerComponent>(out var player) || player.Value == null)
				return;
			var view = player.Value.BodyAnimator;
			if (view == null)
				return;

			int playerEntity = player.Value.Entity;
			var healthPool = world.GetPool<HealthComponent>();
			float health = healthPool.Has(playerEntity) ? healthPool.Get(playerEntity).CurrentHealth : 1f;
			ref var weapon = ref world.GetAsSingleton<WeaponComponent>();
			world.TryGetAsSingleton<GrenadeStateComponent>(out var grenade);

			if (!_initialized)
			{
				_initialized = true;
				_lastShotCounter = weapon.ShotCounter;
				_lastThrowCounter = grenade.ThrowCounter;
				_lastHealth = health;
			}

			// --- Death: plays even while the game is paused by the fail sequence ---
			bool failing = world.TryGetAsSingleton<FailSequenceComponent>(out var fail)
			               && fail.Phase != FailSequencePhase.Inactive;
			if (health <= 0f || failing)
			{
				view.SetPaused(false);
				view.SetDead(true);
				return;
			}

			// --- Pause (flag, not timeScale) ---
			bool paused = world.GetAsSingleton<PauseStateComponent>().IsPaused;
			view.SetPaused(paused);
			if (paused)
				return;

			// --- Locomotion: world move → player-local (the player faces the cursor, so it strafes) ---
			var input = world.GetAsSingleton<PlayerInputComponent>();
			Vector3 move = input.Move;
			Vector3 local = player.Value.transform.InverseTransformDirection(move);
			var localMove = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f);

			float speed = 0f;
			if (move.sqrMagnitude > 0.01f)
			{
				var movePool = world.GetPool<MoveComponent>();
				var modifierPool = world.GetPool<ModifierOwnerComponent>();
				float baseSpeed = movePool.Has(playerEntity) ? movePool.Get(playerEntity).Speed : 0f;
				float speedMod = modifierPool.Has(playerEntity) ? modifierPool.Get(playerEntity).GetModifier<SpeedModifier>() : 1f;
				speed = baseSpeed * speedMod;
			}
			view.SetLocomotion(localMove, speed);

			// --- Weapon set / firing ---
			view.SetWeapon(weapon.GunConfig);

			view.SetFiring(weapon.IsFiring);
			if (weapon.ShotCounter != _lastShotCounter)
			{
				_lastShotCounter = weapon.ShotCounter;
				view.TriggerFire();
			}

			// --- Reload ---
			bool reloading = world.TryGetAsSingleton<ReloadingComponent>(out var reload) && reload.ReloadTime > 0f;
			view.SetReloading(reloading, weapon.GunConfig != null ? weapon.GunConfig.ReloadTime : 0f);

			// --- Melee ---
			if (input.IsMeleeing)
				view.TriggerMelee();

			// --- Grenade ---
			view.SetChargingThrow(grenade.IsCharging && grenade.Count > 0);
			if (grenade.ThrowCounter != _lastThrowCounter)
			{
				_lastThrowCounter = grenade.ThrowCounter;
				view.TriggerThrow();
			}

			// --- Hit reaction ---
			if (_hitCooldown > 0f)
				_hitCooldown -= Time.deltaTime;
			if (health < _lastHealth && _hitCooldown <= 0f)
			{
				view.TriggerHit();
				_hitCooldown = HitReactionInterval;
			}
			_lastHealth = health;
		}
	}
}
