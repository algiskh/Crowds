using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class PlayerMovementSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var inertiaPool = world.GetPool<InertiaComponent>();
			// Pause menu: no movement at all — momentum on the ice is kept (InertiaComponent untouched) for after it.
			if (world.IsWorldFrozen())
				return;
			// Win/fail pause, death, locked controls: the player stops dead, the ice slide included (it kept the player
			// gliding behind the end windows — input was zeroed, the inertia wasn't).
			if (IsStopped(world))
			{
				foreach (var entity in world.Filter<PlayerComponent>().Inc<InertiaComponent>().End())
					Inertia.Stop(ref inertiaPool.Get(entity));
				return;
			}
			// Clamped: a long frame (hitch, return to the window) must not throw the player forward.
			float dt = world.MoveDeltaTime();
			// PlayerConfig.IceInertia: the player is heavier on the ice than the zone's grip (grip / inertia).
			float iceInertia = world.GetAsSingleton<MainHolderComponent>().Value.PlayerConfig?.IceInertia ?? 1f;
			var inputPool = world.GetPool<PlayerInputComponent>();
			var movementPool = world.GetPool<MoveComponent>();
			var playerPool = world.GetPool<PlayerComponent>();
			var modifierPool = world.GetPool<ModifierOwnerComponent>();

			var filter = world.Filter<PlayerComponent>().Inc<PlayerInputComponent>().Inc<MoveComponent>().Inc<ModifierOwnerComponent>().End();
			foreach (var entity in filter)
			{
				ref var input = ref inputPool.Get(entity);

				Vector3 dir = new Vector3(input.Move.x, input.Move.y, input.Move.z);
				bool hasInput = dir.sqrMagnitude > 0.01f;
				ref var modifierOwner = ref modifierPool.Get(entity);
				// Ice (TractionModifier): inertial movement — keeps sliding without input, turns/stops slowly.
				float grip = Inertia.GetGrip(modifierOwner);
				if (!float.IsPositiveInfinity(grip) && iceInertia > 0f)
					grip /= iceInertia;
				bool inertial = !float.IsPositiveInfinity(grip) || inertiaPool.Has(entity);
				if (!hasInput && !inertial)
					continue;

				ref var movement = ref movementPool.Get(entity);
				ref var player = ref playerPool.Get(entity);
				var speedMod = modifierOwner.GetModifier<SpeedModifier>();
				Vector3 desired = hasInput ? dir.normalized * movement.Speed * speedMod : Vector3.zero;
				var playerTransform = player.Value.transform;
				playerTransform.position += inertial
					// Skates (level bonus override, SkatesModifier): counter-steering beats the slide faster.
					? Inertia.Displacement(inertiaPool, entity, playerTransform.position, desired, grip, dt,
						modifierOwner.GetModifier<SkatesModifier>())
					: desired * dt;
			}
		}

		private static bool IsStopped(EcsWorld world)
		{
			if (world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused)
				return true;
			if (world.TryGetAsSingleton<InputLockComponent>(out var inputLock) && inputLock.Locked)
				return true;
			return world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive;
		}
	}
}
