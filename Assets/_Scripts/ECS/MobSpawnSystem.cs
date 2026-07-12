using Leopotam.EcsLite;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	public class MobSpawnSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			var spawnRequestPool = world.GetPool<MobSpawnRequestComponent>();
			var filter = world.Filter<MobSpawnRequestComponent>().End();

			if (filter.GetEntitiesCount() == 0)
				return;

			foreach (var spawnEntity in filter)
			{
				ref var spawnRequest = ref spawnRequestPool.Get(spawnEntity);
				var mobConfig = spawnRequest.Config;
				var spawnPoint = spawnRequest.SpawnPoint;

				Vector3 spawnPos = spawnPoint.position;
				if (NavMesh.SamplePosition(spawnPos, out var navHit, 2f, NavMesh.AllAreas))
					spawnPos = navHit.position;

				CreateMob(world, mobConfig, spawnPos);
				world.DelEntity(spawnEntity);
			}
		}

		/// <summary>
		/// Создаёт сущность моба со всеми базовыми компонентами в заданной позиции и возвращает её id.
		/// Позиция должна быть уже снапнута на navmesh вызывающим. Используется и обычным спауном
		/// (MobSpawnSystem), и групповым (GroupSpawnSystem), чтобы не дублировать инициализацию.
		/// </summary>
		public static int CreateMob(EcsWorld world, MobConfig mobConfig, Vector3 position)
		{
			ref var mobPool = ref world.GetAsSingleton<MobPoolComponent>();
			var mainHolder = world.GetAsSingleton<MainHolderComponent>().Value;
			var playerPosition = world.GetAsSingleton<PlayerComponent>().Value.transform.position;

			float recalcInterval = mainHolder.PathRecalculationInterval;
			float now = Time.time;

			Mob mob = SpawnMob(ref mobPool, mobConfig);
			mob.transform.position = position;
			// Per-config size: one prefab can back many configs of different scale. Applied every spawn,
			// so pooled reuse always re-establishes the right size. Scales render + collider together.
			mob.transform.localScale = Vector3.one * mobConfig.Scale;

			var mobEntity = world.NewEntity();

			ref var mobComponent = ref world.GetPool<MobComponent>().Add(mobEntity);
			ref var moveComponent = ref world.GetPool<MoveComponent>().Add(mobEntity);
			ref var modifierComponent = ref world.GetPool<ModifierOwnerComponent>().Add(mobEntity);
			ref var healthComponent = ref world.GetPool<HealthComponent>().Add(mobEntity);
			ref var colliderComponent = ref world.GetPool<ColliderComponent>().Add(mobEntity);
			ref var pathRecalculationComponent = ref world.GetPool<PathRecalculation>().Add(mobEntity);
			ref var looker = ref world.GetPool<LookerAtCamera>().Add(mobEntity);

			modifierComponent.Entity = mobEntity;
			modifierComponent.Transform = mob.transform;
			modifierComponent.Modifiers = new();
			mobComponent.Value = mob;
			mobComponent.Config = mobConfig;
			mobComponent.Cooldown = 0;

			Vector3 toPlayer = playerPosition - position;
			toPlayer.y = 0f;
			moveComponent.Direction = toPlayer.sqrMagnitude > 0.0001f ? toPlayer.normalized : Vector3.forward;
			moveComponent.Speed = mobConfig.Speed;
			moveComponent.Transform = mob.transform;

			healthComponent.CurrentHealth = mobConfig.Health;
			healthComponent.MaxHealth = mobConfig.Health;
			healthComponent.TargetType = mobConfig.TargetType;

			colliderComponent.CollisionType = CollisionType.Mob;
			colliderComponent.Value = mob.Collider;

			pathRecalculationComponent.Interval = recalcInterval;
			// Джиттер: разносим первый пересчёт у пачки мобов, чтобы не пересчитывать всех в один кадр.
			pathRecalculationComponent.LastTime = now - Random.Range(0f, recalcInterval);

			looker.Transform = mob.ValueBar != null ? mob.ValueBar.Transform : null;
			looker.FlatBillboard = true;

			// Моб-гренадёр: тот же моб + поведение броска гранат (GrenadierSystem).
			if (mobConfig is GrenadierMobConfig grenadierConfig)
			{
				ref var grenadier = ref world.GetPool<GrenadierComponent>().Add(mobEntity);
				grenadier.Config = grenadierConfig;
				grenadier.State = GrenadierState.Chase;
				grenadier.Timer = 0f;
				grenadier.HasFleeTarget = false;
			}
			// Моб ближнего боя: тот же моб + телеграфированная атака (MeleeAttackerSystem).
			else if (mobConfig is MeleeMobConfig meleeMobConfig)
			{
				ref var attacker = ref world.GetPool<MeleeAttackerComponent>().Add(mobEntity);
				attacker.Config = meleeMobConfig;
				attacker.State = MeleeAttackerState.Chase;
				attacker.Timer = 0f;
			}
			// Моб-стрелок: тот же моб + телеграфированный выстрел (RangedAttackerSystem).
			else if (mobConfig is RangedMobConfig rangedMobConfig)
			{
				ref var ranged = ref world.GetPool<RangedAttackerComponent>().Add(mobEntity);
				ranged.Config = rangedMobConfig;
				ranged.State = RangedAttackerState.Chase;
				ranged.Timer = 0f;
			}

			// Crowd rendering: this mob is drawn GPU-instanced from a baked VAT (CrowdRenderSystem)
			// instead of its SkinnedMeshRenderer+Animator, which we switch off here.
			if (mobConfig.CrowdLibrary != null)
			{
				ref var crowd = ref world.GetPool<CrowdInstanceComponent>().Add(mobEntity);
				crowd.Library = mobConfig.CrowdLibrary;
				crowd.CurrentClip = Scene.Animation.AnimationType.Run;
				crowd.ClipTime = 0f;
				crowd.Initialized = false;
				crowd.Tint = mobConfig.Tint; // fed to CrowdVat _InstColor in CrowdRenderSystem
				DisableSkinnedView(mob);
			}
			else
			{
				// Classic skinned mob: tint via MaterialPropertyBlock (no material instances, keeps batching).
				ApplySkinnedTint(mob, mobConfig.Tint);
			}

			InitializeMobGameObject(mob, mobConfig, playerPosition);
			return mobEntity;
		}

		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
		private static MaterialPropertyBlock _tintMpb;

		/// <summary>
		/// Tints a classic skinned mob by multiplying each SkinnedMeshRenderer's material _BaseColor by the
		/// config tint via a shared MaterialPropertyBlock — no per-mob material instances, so instancing/
		/// batching is preserved. Multiply semantics match the VAT path (white tint = unchanged). Health bar
		/// (a separate MeshRenderer) is untouched. Assumes a URP-Lit-style _BaseColor property.
		/// </summary>
		private static void ApplySkinnedTint(Mob mob, Color tint)
		{
			var renderers = mob.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			if (renderers.Length == 0)
				return;

			_tintMpb ??= new MaterialPropertyBlock();
			for (int i = 0; i < renderers.Length; i++)
			{
				var r = renderers[i];
				var mat = r.sharedMaterial;
				Color baseColor = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
				r.GetPropertyBlock(_tintMpb);
				_tintMpb.SetColor(BaseColorId, baseColor * tint);
				r.SetPropertyBlock(_tintMpb);
			}
		}

		/// <summary>
		/// Turns off the skinned renderer(s) and Animator so a crowd mob costs nothing to skin/animate on
		/// the CPU — CrowdRenderSystem draws its baked pose instead. The health bar (separate MeshRenderer)
		/// is untouched. Stays off across pool reuse, so calling it again on a pooled mob is a cheap no-op.
		/// </summary>
		private static void DisableSkinnedView(Mob mob)
		{
			var skinned = mob.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			for (int i = 0; i < skinned.Length; i++)
				skinned[i].enabled = false;

			var animators = mob.GetComponentsInChildren<Animator>(true);
			for (int i = 0; i < animators.Length; i++)
				animators[i].enabled = false;
		}

		/// <summary>
		/// Берёт моба по id из стека пула или инстанцирует нового.
		/// </summary>
		private static Mob SpawnMob(ref MobPoolComponent mobPool, MobConfig mobConfig)
		{
			if (mobPool.Pools == null)
				mobPool.Pools = new Dictionary<string, Stack<Mob>>();

			if (mobPool.Pools.TryGetValue(mobConfig.Id, out var stack) && stack.Count > 0)
				return stack.Pop();

			var mob = Object.Instantiate(mobConfig.Prefab, mobPool.Parent);
			mob.SetId(mobConfig.Id);
			return mob;
		}

		private static void InitializeMobGameObject(Mob mob, MobConfig mobConfig, Vector2 playerPosition)
		{
			mob.ValueBar.SetMaxValue(mobConfig.Health)
						.ApplyValue(mobConfig.Health)
						.SetVisible(true);

			mob.gameObject.SetActive(true);
		}
	}
}