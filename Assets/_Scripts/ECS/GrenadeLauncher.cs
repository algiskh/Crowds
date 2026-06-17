using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Общий код запуска летящей гранаты (GrenadeProjectileComponent) из пула.
	/// Используется и игроком (GrenadeThrowSystem), и мобом-гренадёром (GrenadierSystem):
	/// единственное различие — точка старта/цель. Кого и насколько задевает взрыв задаётся
	/// долями урона из самого GrenadeConfig (MobDamageScale / PlayerDamageScale) — по умолчанию
	/// граната опасна всем. Если у конфига задан трейл-эффект, он достаётся из пула, делается
	/// ребёнком гранаты и летит с ней; снаряд вернёт его в пул при взрыве (см. GrenadeProjectileSystem).
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
				return; // префаб гранаты не назначен в MainHolder — бросать нечем

			grenade.transform.position = start;
			grenade.gameObject.SetActive(true);

			// --- Трейл-эффект, сопровождающий гранату ---
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
