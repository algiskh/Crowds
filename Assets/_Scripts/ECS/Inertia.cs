using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Inertial ("on ice") movement shared by PlayerMovementSystem and MoveSystem. While the owner carries a
	/// TractionModifier (grip, m/s²), the movement systems don't move it by the desired velocity directly: the real
	/// velocity (InertiaComponent) is steered toward the desired one by at most grip·dt per frame, so the owner keeps
	/// sliding, turns wide and stops slowly. Without the modifier the velocity just tracks the desired one, so momentum
	/// carries over when stepping onto the ice.
	/// If last frame's step was mostly blocked (a wall / navmesh edge clamped the NavMeshAgent, or the crowd), the
	/// velocity is reset to what actually moved — no endless pushing into the boards. See Docs/HockeyLevelFeature.md.
	/// </summary>
	public static class Inertia
	{
		/// <summary>Grip value meaning "no TractionModifier" (normal ground).</summary>
		public const float NoGrip = float.PositiveInfinity;

		// The "blocked step" check only runs above this speed: a tiny step (start of an acceleration) can be eaten
		// by the NavMeshAgent sync / float noise, and resetting the velocity then would never let it grow.
		private const float MinBlockCheckSpeed = 1f;

		/// <summary>Grip of the owner: the lowest TractionModifier value, or <see cref="NoGrip"/>.</summary>
		public static float GetGrip(in ModifierOwnerComponent owner) => owner.GetMinModifier<TractionModifier>(NoGrip);

		/// <summary>
		/// This frame's displacement for <paramref name="entity"/>. Adds InertiaComponent lazily, only once the entity
		/// first meets slippery ground (no cost for everybody else).
		/// </summary>
		public static Vector3 Displacement(EcsPool<InertiaComponent> pool, int entity, Vector3 position,
			Vector3 desiredVelocity, float grip, float dt)
		{
			bool slippery = !float.IsPositiveInfinity(grip);
			if (!pool.Has(entity))
			{
				if (!slippery)
					return desiredVelocity * dt;
				ref var added = ref pool.Add(entity);
				// EcsLite doesn't reset a recycled component: initialize every field. Momentum = the current intent.
				added.Velocity = new Vector3(desiredVelocity.x, 0f, desiredVelocity.z);
				added.PrevPosition = position;
				added.LastStep = Vector3.zero;
				added.LastDt = 0f;
				added.Sliding = false;
			}
			return Step(ref pool.Get(entity), position, desiredVelocity, grip, dt);
		}

		public static Vector3 Step(ref InertiaComponent state, Vector3 position, Vector3 desiredVelocity, float grip, float dt)
		{
			desiredVelocity.y = 0f;
			if (float.IsPositiveInfinity(grip) || dt <= 0f)
			{
				state.Velocity = desiredVelocity;
				state.Sliding = false;
				return desiredVelocity * dt;
			}

			if (state.Sliding && state.LastDt > 0f)
			{
				Vector3 moved = position - state.PrevPosition;
				moved.y = 0f;
				float expected = state.LastStep.magnitude;
				if (expected > MinBlockCheckSpeed * state.LastDt && moved.magnitude < expected * 0.5f)
					state.Velocity = moved / state.LastDt; // blocked: keep only what really moved (slides along walls)
			}

			state.Velocity = Vector3.MoveTowards(state.Velocity, desiredVelocity, grip * dt);
			Vector3 step = state.Velocity * dt;
			state.PrevPosition = position;
			state.LastStep = step;
			state.LastDt = dt;
			state.Sliding = true;
			return step;
		}
	}
}
