using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Moves thrown grenades along a parabolic arc from Start to Target.
	/// Height is added as a parabola peaking mid-flight.
	/// On landing (t >= 1) creates a RequestExplosionComponent at the target point
	/// and returns the grenade to the pool.
	/// </summary>
	public sealed class GrenadeProjectileSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			var projectilePool = world.GetPool<GrenadeProjectileComponent>();
			ref var grenadePool = ref world.GetAsSingleton<GrenadePoolComponent>();
			var effectPool = world.GetAsSingleton<EffectPoolComponent>();

			var filter = world.Filter<GrenadeProjectileComponent>().End();
			foreach (var entity in filter)
			{
				ref var proj = ref projectilePool.Get(entity);

				proj.Elapsed += Time.deltaTime;
				float t = proj.FlightTime > 0f
					? Mathf.Clamp01(proj.Elapsed / proj.FlightTime)
					: 1f;

				if (proj.Value != null)
				{
					Vector3 pos = Vector3.Lerp(proj.Start, proj.Target, t);
					pos.y += proj.ArcHeight * 4f * t * (1f - t); // parabola, peak at t = 0.5
					proj.Value.transform.position = pos;
				}

				if (t < 1f)
					continue;

				// --- Landing: on-demand explosion at the target point ---
				ref var explosion = ref world.CreateSimpleEntity<RequestExplosionComponent>();
				explosion.Position = proj.Target;
				explosion.Radius = proj.Radius;
				explosion.MaxDamage = proj.MaxDamage;
				explosion.MinDamage = proj.MinDamage;
				explosion.Delay = proj.FuseDelay;
				explosion.EffectId = proj.EffectId;
				explosion.MobDamageScale = proj.MobDamageScale;
				explosion.PlayerDamageScale = proj.PlayerDamageScale;

				// First detach the trail effect from the grenade and return it to the pool,
				// then remove the grenade itself (order matters: the effect is a child of the grenade).
				if (proj.TrailEffect != null)
				{
					effectPool.Pool(proj.TrailEffect);
					proj.TrailEffect = null;
				}

				if (proj.Value != null)
				{
					proj.Value.gameObject.SetActive(false);
					grenadePool.Value?.Push(proj.Value);
				}

				world.DelEntity(entity);
			}
		}
	}
}
