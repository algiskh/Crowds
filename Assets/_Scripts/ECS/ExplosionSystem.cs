using System.Collections.Generic;
using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Взрыв по требованию: обрабатывает RequestExplosionComponent.
	/// Пока Delay (фитиль) > 0 — обратный отсчёт. На детонации:
	///  - запрашивает пулевой эффект взрыва (RequestEffectComponent);
	///  - наносит радиальный урон мобам в круге: максимум в центре,
	///    минимум на краю радиуса (линейный спад).
	/// </summary>
	public sealed class ExplosionSystem : IEcsRunSystem
	{
		private const int BUFFER_SIZE = 64;

		private readonly Collider[] _overlapBuffer = new Collider[BUFFER_SIZE];
		private readonly Dictionary<Collider, int> _mobColliderMap = new Dictionary<Collider, int>(64);

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			if (pauseState.IsPaused)
				return;

			var explosionPool = world.GetPool<RequestExplosionComponent>();
			var filter = world.Filter<RequestExplosionComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;

			int layerMask = ~0;
			if (world.TryGetAsSingleton<MainHolderComponent>(out var mainHolder) && mainHolder.Value != null)
				layerMask = mainHolder.Value.MobLayerMask.value;

			var mobPool = world.GetPool<MobComponent>();
			var colliderPool = world.GetPool<ColliderComponent>();

			// collider → mob entity (строим один раз за кадр, когда есть взрывы).
			_mobColliderMap.Clear();
			var mobFilter = world.Filter<MobComponent>().Inc<ColliderComponent>().End();
			foreach (var mobEntity in mobFilter)
			{
				ref var col = ref colliderPool.Get(mobEntity);
				if (col.CollisionType == CollisionType.Mob && col.Value != null)
					_mobColliderMap[col.Value] = mobEntity;
			}

			foreach (var entity in filter)
			{
				ref var request = ref explosionPool.Get(entity);

				if (request.Delay > 0f)
				{
					request.Delay -= Time.deltaTime;
					if (request.Delay > 0f)
						continue;
				}

				// --- Эффект взрыва ---
				ref var effectRequest = ref world.CreateSimpleEntity<RequestEffectComponent>();
				effectRequest.EffectId = string.IsNullOrEmpty(request.EffectId) ? "explosion" : request.EffectId;
				effectRequest.Position = request.Position;

				// --- Радиальный урон по мобам (доля MobDamageScale) ---
				if (request.MobDamageScale > 0f && request.Radius > 0f)
				{
					int hitCount = Physics.OverlapSphereNonAlloc(
						request.Position, request.Radius, _overlapBuffer, layerMask, QueryTriggerInteraction.Collide);

					for (int i = 0; i < hitCount; i++)
					{
						var col = _overlapBuffer[i];
						if (col == null) continue;
						if (!_mobColliderMap.TryGetValue(col, out var mobEntity)) continue;
						if (!mobPool.Has(mobEntity)) continue;

						ref var mob = ref mobPool.Get(mobEntity);
						float dist = Vector3.Distance(request.Position, mob.Value.transform.position);
						float t = Mathf.Clamp01(dist / request.Radius);
						float damage = Mathf.Lerp(request.MaxDamage, request.MinDamage, t) * request.MobDamageScale;

						ref var damageRequest = ref world.CreateSimpleEntity<RequestDamageComponent>();
						damageRequest.TargetEntity = mobEntity;
						damageRequest.Damage = damage;
					}
				}

				// --- Радиальный урон по игроку (доля PlayerDamageScale) ---
				if (request.PlayerDamageScale > 0f && request.Radius > 0f &&
					world.TryGetAsSingleton<PlayerComponent>(out var playerSingleton))
				{
					Vector3 playerPos = playerSingleton.Value.transform.position;
					float dist = Vector3.Distance(request.Position, playerPos);
					if (dist <= request.Radius)
					{
						float t = Mathf.Clamp01(dist / request.Radius);
						float damage = Mathf.Lerp(request.MaxDamage, request.MinDamage, t) * request.PlayerDamageScale;

						ref var damageRequest = ref world.CreateSimpleEntity<RequestDamageComponent>();
						damageRequest.TargetEntity = playerSingleton.Value.Entity;
						damageRequest.Damage = damage;
					}
				}

				world.DelEntity(entity);
			}
		}
	}
}
