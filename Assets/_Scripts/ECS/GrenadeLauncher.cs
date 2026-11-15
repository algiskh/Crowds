using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Shared code for launching a flying grenade (GrenadeProjectileComponent) from the pool.
	/// Used by both the player (GrenadeThrowSystem) and the grenadier mob (GrenadierSystem):
	/// the only difference is the start point/target. Who the explosion hits and how hard is defined
	/// by the damage scales from the GrenadeConfig itself (MobDamageScale / PlayerDamageScale) — by default
	/// the grenade is dangerous to everyone. If the config has a trail effect, it's popped from the pool,
	/// made a child of the grenade and flies with it; the projectile returns it to the pool on explosion
	/// (see GrenadeProjectileSystem).
	/// </summary>
	public static class GrenadeLauncher
	{
		public static void Launch(EcsWorld world, MainHolder mainHolder, GrenadeConfig grenadeConfig,
			Vector3 start, Vector3 target)
		{
			if (grenadeConfig == null)
				return;

			ref var pool = ref world.GetAsSingleton<GrenadePoolComponent>();
			Grenade grenade;
			if (pool.Value != null && pool.Value.Count > 0)
				grenade = pool.Value.Pop();
			else
				grenade = Object.Instantiate(mainHolder.GrenadePrefab, pool.Parent);

			if (grenade == null)
				return; // grenade prefab not assigned in MainHolder — nothing to throw

			grenade.transform.position = start;
			grenade.gameObject.SetActive(true);

			// --- Trail effect accompanying the grenade ---
			SceneEffect trail = null;
			if (!string.IsNullOrEmpty(grenadeConfig.TrailEffectId))
			{
				var effectPool = world.GetAsSingleton<EffectPoolComponent>();
				var wrapper = mainHolder.EffectsHolder.GetEffect(grenadeConfig.TrailEffectId);
				trail = effectPool.SpawnFromPool(wrapper);
				if (trail != null)
				{
					trail.SetParent(grenade.transform);
					trail.transform.localPosition = Vector3.zero;
					trail.Show();
				}
			}

			var entity = world.NewEntity();
			ref var proj = ref world.GetPool<GrenadeProjectileComponent>().Add(entity);
			proj.Value = grenade;
			proj.Start = start;
			proj.Target = target;
			proj.Elapsed = 0f;

			float dist = Vector3.Distance(start, target);
			proj.FlightTime = Mathf.Max(0.15f, dist / Mathf.Max(0.01f, grenadeConfig.ThrowSpeed));
			proj.ArcHeight = grenadeConfig.ArcHeight;

			proj.Radius = grenadeConfig.Radius;
			proj.MaxDamage = grenadeConfig.MaxDamage;
			proj.MinDamage = grenadeConfig.MinDamage;
			proj.FuseDelay = grenadeConfig.FuseDelay;
			proj.EffectId = grenadeConfig.ExplosionEffectId;
			proj.MobDamageScale = grenadeConfig.MobDamageScale;
			proj.PlayerDamageScale = grenadeConfig.PlayerDamageScale;
			proj.TrailEffect = trail;
		}
	}
}
