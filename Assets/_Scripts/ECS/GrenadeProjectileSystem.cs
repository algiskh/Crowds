using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Двигает брошенные гранаты по параболической дуге от Start к Target.
	/// Высота добавляется как парабола с пиком в середине полёта.
	/// При приземлении (t >= 1) создаёт RequestExplosionComponent в точке цели
	/// и возвращает гранату в пул.
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
					pos.y += proj.ArcHeight * 4f * t * (1f - t); // парабола, пик в t = 0.5
					proj.Value.transform.position = pos;
				}

				if (t < 1f)
					continue;

				// --- Приземление: взрыв по требованию в точке цели ---
				ref var explosion = ref world.CreateSimpleEntity<RequestExplosionComponent>();
				explosion.Position = proj.Target;
				explosion.Radius = proj.Radius;
				explosion.MaxDamage = proj.MaxDamage;
				explosion.MinDamage = proj.MinDamage;
				explosion.Delay = proj.FuseDelay;
				explosion.EffectId = proj.EffectId;
				explosion.MobDamageScale = proj.MobDamageScale;
				explosion.PlayerDamageScale = proj.PlayerDamageScale;

				// Сначала снимаем трейл-эффект с гранаты и возвращаем его в пул,
				// затем убираем саму гранату (порядок важен: эффект — ребёнок гранаты).
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
