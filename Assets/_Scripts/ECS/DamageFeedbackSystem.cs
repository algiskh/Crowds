using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Shows the player's injuries on screen (GameDesignReview, "Получение урона"):
	///  • a red edge vignette flashing in proportion to the hit (share of max HP; DoT ticks flash weaker);
	///  • a permanent pulse while health is below PlayerConfig.DamageFeedback.LowHealthThreshold;
	///  • a short camera shake on direct hits (CameraShakeSystem);
	///  • an arc at the screen edge pointing toward the hit source (RequestDamageComponent.HasSource).
	///
	/// Input: PlayerDamagedEventComponent entities raised by DamageSystem (one per applied hit), deleted here.
	/// Runs after DamageSystem. Freezes while paused; hides everything once the fail sequence starts (its own
	/// red veil takes over).
	/// </summary>
	public sealed class DamageFeedbackSystem : IEcsRunSystem
	{
		private DamageFeedbackOverlay _overlay;
		private float _flash;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var settings = world.GetAsSingleton<MainHolderComponent>().Value.PlayerConfig?.DamageFeedback;
			if (settings == null)
			{
				world.DeleteAllWith<PlayerDamagedEventComponent>();
				return;
			}

			// Created lazily; its GameObject lives in the scene, so a restart (scene reload) recreates both.
			var overlay = _overlay ??= new DamageFeedbackOverlay();
			overlay.EnsureCreated(settings.VignetteColor);

			// Death cinematic: the fail veil owns the screen.
			if (world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive)
			{
				_flash = 0f;
				overlay.HideAll();
				world.DeleteAllWith<PlayerDamagedEventComponent>();
				return;
			}

			bool paused = world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused;
			float dt = paused ? 0f : Time.deltaTime;

			// --- New hits ---
			Camera cam = world.TryGetAsSingleton<CameraComponent>(out var camComponent) ? camComponent.Value : null;
			Vector3 playerPos = world.TryGetAsSingleton<PlayerComponent>(out var player) && player.Value != null
				? player.Value.transform.position
				: Vector3.zero;

			var eventPool = world.GetPool<PlayerDamagedEventComponent>();
			var filter = world.Filter<PlayerDamagedEventComponent>().End();
			foreach (var entity in filter)
			{
				ref var hit = ref eventPool.Get(entity);
				float maxHealth = Mathf.Max(1f, hit.MaxHealth);
				// 0..1: how "big" this hit is relative to the full-flash threshold.
				float strength = Mathf.Clamp01(hit.Damage / maxHealth / settings.FullFlashDamageShare);

				float flash = hit.IsPeriodic
					? strength * settings.MaxFlashAlpha * settings.PeriodicFlashScale
					: Mathf.Lerp(settings.MinFlashAlpha, settings.MaxFlashAlpha, strength);
				_flash = Mathf.Max(_flash, flash);

				if (!hit.IsPeriodic)
				{
					if (settings.ShakeDuration > 0f && settings.ShakeAmplitude > 0f)
						CameraShakeSystem.Shake(world, settings.ShakeDuration, settings.ShakeAmplitude * Mathf.Lerp(0.4f, 1f, strength));

					if (settings.ShowHitDirection && hit.HasSource && cam != null &&
						TryGetScreenAngle(cam, playerPos, hit.SourcePosition, out float angle))
						overlay.ShowHitDirection(angle, settings.HitDirectionTime, strength);
				}

				world.DelEntity(entity);
			}

			// --- Vignette: fading flash + low-health pulse ---
			if (dt > 0f)
				_flash = Mathf.MoveTowards(_flash, 0f, settings.MaxFlashAlpha / settings.FlashFadeTime * dt);

			float pulse = 0f;
			if (settings.LowHealthThreshold > 0f && world.TryGetAsSingleton<PlayerComponent>(out var p) &&
				p.Value != null && world.GetPool<HealthComponent>().Has(p.Value.Entity))
			{
				ref var health = ref world.GetPool<HealthComponent>().Get(p.Value.Entity);
				float share = health.MaxHealth > 0f ? health.CurrentHealth / health.MaxHealth : 1f;
				if (share > 0f && share < settings.LowHealthThreshold)
				{
					// Closer to death → stronger pulse.
					float danger = 1f - share / settings.LowHealthThreshold;
					float wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * settings.LowHealthPulseRate * Mathf.PI * 2f);
					pulse = settings.LowHealthPulseAlpha * Mathf.Lerp(0.45f, 1f, danger) * Mathf.Lerp(0.35f, 1f, wave);
				}
			}

			overlay.SetVignetteAlpha(Mathf.Max(_flash, pulse));
			overlay.Tick(dt);
		}

		// Screen-space angle (deg, 0 = right, 90 = up) from the player toward the hit source.
		private static bool TryGetScreenAngle(Camera cam, Vector3 playerPos, Vector3 sourcePos, out float angle)
		{
			angle = 0f;
			Vector3 flat = sourcePos - playerPos;
			flat.y = 0f;
			if (flat.sqrMagnitude < 0.01f)
				return false;

			// Project a point 1 m from the player toward the source, so a source behind the camera stays valid.
			Vector3 a = cam.WorldToScreenPoint(playerPos);
			Vector3 b = cam.WorldToScreenPoint(playerPos + flat.normalized);
			Vector2 dir = new(b.x - a.x, b.y - a.y);
			if (dir.sqrMagnitude < 0.0001f)
				return false;
			angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
			return true;
		}
	}
}
