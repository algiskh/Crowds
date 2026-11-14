using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Pause menu freeze of the things that run outside ECS time: on entering <see cref="Extensions.IsWorldFrozen"/>
	/// every playing Animator (mobs, props; not the player body — PlayerAnimator pauses itself) gets speed 0 and every
	/// playing ParticleSystem is paused; on leaving, exactly those are restored. ECS-driven motion and timers stop via
	/// <see cref="Extensions.GameDeltaTime"/> / early returns. Registered last (after PauseSystem).
	/// </summary>
	public sealed class WorldFreezeSystem : IEcsRunSystem
	{
		private readonly List<(Animator animator, float speed)> _animators = new();
		private readonly List<ParticleSystem> _particles = new();
		private bool _frozen;

		public void Run(IEcsSystems systems)
		{
			bool frozen = systems.GetWorld().IsWorldFrozen();
			if (frozen == _frozen)
				return;
			_frozen = frozen;
			if (frozen) Freeze();
			else Unfreeze();
		}

		private void Freeze()
		{
			foreach (var animator in Object.FindObjectsByType<Animator>(FindObjectsSortMode.None))
			{
				if (animator.speed <= 0f || animator.GetComponent<PlayerAnimator>() != null)
					continue;
				_animators.Add((animator, animator.speed));
				animator.speed = 0f;
			}
			foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
			{
				if (!ps.isPlaying)
					continue;
				ps.Pause(false);
				_particles.Add(ps);
			}
		}

		private void Unfreeze()
		{
			foreach (var (animator, speed) in _animators)
				if (animator != null && animator.speed <= 0f)
					animator.speed = speed;
			foreach (var ps in _particles)
				if (ps != null && ps.isPaused)
					ps.Play(false);
			_animators.Clear();
			_particles.Clear();
		}
	}
}
