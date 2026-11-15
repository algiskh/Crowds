using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Additive camera shake that doesn't fight FollowSystem. Registered twice:
	///  • <c>new CameraShakeSystem(apply: false)</c> right before FollowSystem — removes last frame's offset, so
	///    following (and LookAtCursor's cursor ray) works from the clean camera position;
	///  • <c>new CameraShakeSystem(apply: true)</c> at the end of the frame — applies this frame's offset, so the
	///    frame is rendered shaken.
	/// Trigger with <see cref="Shake"/>. Decays linearly over its duration; frozen (no offset) while paused.
	/// State lives in the CameraShakeComponent singleton (camera = the CameraComponent's transform, resolved lazily).
	/// </summary>
	public sealed class CameraShakeSystem : IEcsRunSystem
	{
		private readonly bool _apply;

		public CameraShakeSystem(bool apply)
		{
			_apply = apply;
		}

		/// <summary>Starts (or strengthens) a shake. A weaker shake never cuts a stronger running one short.</summary>
		public static void Shake(EcsWorld world, float duration, float amplitude)
		{
			if (duration <= 0f || amplitude <= 0f)
				return;
			ref var shake = ref GetOrCreate(world);
			float remaining = shake.Duration > 0f ? shake.Amplitude * (shake.Timer / shake.Duration) : 0f;
			if (amplitude < remaining)
				return;
			shake.Amplitude = amplitude;
			shake.Duration = duration;
			shake.Timer = duration;
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			if (!world.TryGetAsSingleton<CameraShakeComponent>(out _))
				return; // nobody has shaken yet

			ref var shake = ref world.GetAsSingleton<CameraShakeComponent>();
			if (shake.Camera == null)
			{
				if (!world.TryGetAsSingleton<CameraComponent>(out var cam) || cam.Value == null)
					return;
				shake.Camera = cam.Value.transform;
			}

			if (!_apply)
			{
				// Restore the clean position before FollowSystem moves the camera.
				if (shake.AppliedOffset != Vector3.zero)
				{
					shake.Camera.position -= shake.AppliedOffset;
					shake.AppliedOffset = Vector3.zero;
				}
				return;
			}

			bool paused = world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused;
			if (paused || shake.Timer <= 0f)
				return;

			shake.Timer -= Time.deltaTime;
			float k = Mathf.Clamp01(shake.Timer / shake.Duration);
			// Screen-plane jitter: camera right/up, so a top-down camera doesn't zoom in/out.
			Vector2 r = Random.insideUnitCircle * (shake.Amplitude * k);
			Vector3 offset = shake.Camera.right * r.x + shake.Camera.up * r.y;
			shake.Camera.position += offset;
			shake.AppliedOffset = offset;
		}

		private static ref CameraShakeComponent GetOrCreate(EcsWorld world)
		{
			if (world.TryGetAsSingleton<CameraShakeComponent>(out _))
				return ref world.GetAsSingleton<CameraShakeComponent>();
			return ref world.CreateSimpleEntity<CameraShakeComponent>();
		}
	}
}
