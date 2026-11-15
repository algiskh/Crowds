using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Reads the hold of the "Throw" action: the longer it's held — the farther the throw.
	/// Range interpolates between Min/MaxThrowDistance from PlayerConfig (player mechanic),
	/// while speed/damage/radius/effects come from the current GrenadeConfig (grenade type).
	/// Direction is taken from the player to the aim point (under the cursor/stick) — using the same
	/// trick as weapon aiming (ray from the cursor into the plane at the player's height).
	/// While charging, shows the expected landing point (GrenadeAimVisualizer).
	/// On release spends a grenade and throws an arcing projectile (GrenadeProjectileComponent);
	/// if the config has a trail effect, it's made a child of the grenade and flies with it.
	/// </summary>
	public sealed class GrenadeThrowSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
			if (pauseState.IsPaused || inputLock.Locked)
				return;

			ref var inputActions = ref world.GetAsSingleton<InputActionsComponent>();
			var throwAction = inputActions.ThrowAction;
			if (throwAction == null)
				return;

			ref var grenade = ref world.GetAsSingleton<GrenadeStateComponent>();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			var config = mainHolder.Value.PlayerConfig;
			var grenadeConfig = grenade.CurrentConfig;

			GrenadeAimVisualizer visualizer = null;
			if (world.TryGetAsSingleton<GrenadeAimVisualizerComponent>(out var vis))
				visualizer = vis.Value;

			bool isHeld = throwAction.ReadValue<float>() > 0.5f;
			bool canThrow = grenade.Count > 0 && grenadeConfig != null;

			if (isHeld)
			{
				grenade.IsCharging = true;
				grenade.ChargeTime += Time.deltaTime;

				// Landing-point preview — only when there's something to throw and a way to throw it.
				if (canThrow)
				{
					float ratio = ChargeRatio(config, grenade.ChargeTime);
					Vector3 preview = ComputeLandingPosition(world, config, ratio);
					if (visualizer != null)
						visualizer.Show(preview, grenadeConfig.Radius);
				}
				else if (visualizer != null)
				{
					visualizer.Hide();
				}
				return;
			}

			// The button isn't held. If we were charging last frame — this release is a throw.
			if (!grenade.IsCharging)
				return;

			float chargeRatio = ChargeRatio(config, grenade.ChargeTime);
			grenade.IsCharging = false;
			grenade.ChargeTime = 0f;

			if (visualizer != null)
				visualizer.Hide();

			if (!canThrow)
				return;

			Vector3 landing = ComputeLandingPosition(world, config, chargeRatio);

			grenade.Count--;
			grenade.ThrowCounter++;
			world.CreateSimpleEntity<UpdateGrenadeViewRequestComponent>();

			SpawnProjectile(world, mainHolder.Value, grenadeConfig, landing);
		}

		private static float ChargeRatio(PlayerConfig config, float chargeTime)
			=> config.MaxThrowChargeTime > 0f
				? Mathf.Clamp01(chargeTime / config.MaxThrowChargeTime)
				: 1f;

		/// <summary>
		/// Creates a flying projectile from the player to the landing point via the shared GrenadeLauncher.
		/// Who the explosion hits and how hard is defined by the damage scales in GrenadeConfig.
		/// </summary>
		private void SpawnProjectile(EcsWorld world, MainHolder mainHolder, GrenadeConfig grenadeConfig, Vector3 landing)
		{
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			Vector3 start = player.Value.transform.position;

			GrenadeLauncher.Launch(world, mainHolder, grenadeConfig, start, landing);
		}

		/// <summary>
		/// Landing point = player position + aim direction * range(charge).
		/// The direction is computed via a ray from the cursor into the plane at the player's height
		/// (same trick as in AimVisualizer / LookAtCursorSystem).
		/// </summary>
		private Vector3 ComputeLandingPosition(EcsWorld world, PlayerConfig config, float chargeRatio)
		{
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			Vector3 origin = player.Value.transform.position;
			Vector3 aimDir = player.Value.transform.forward;

			if (world.TryGetAsSingleton<CameraComponent>(out var cam) && cam.Value != null &&
				world.TryGetAsSingleton<VirtualAimCursorComponent>(out var cursor))
			{
				Ray ray = cam.Value.ScreenPointToRay(cursor.ScreenPosition);
				var plane = new Plane(Vector3.up, origin);
				if (plane.Raycast(ray, out float enter))
				{
					Vector3 aimWorld = ray.GetPoint(enter);
					Vector3 flat = aimWorld - origin;
					flat.y = 0f;
					if (flat.sqrMagnitude > 0.0001f)
						aimDir = flat.normalized;
				}
			}

			float distance = Mathf.Lerp(config.MinThrowDistance, config.MaxThrowDistance, chargeRatio);

			Vector3 landing = origin + aimDir * distance;
			landing.y = origin.y;
			return landing;
		}
	}
}
