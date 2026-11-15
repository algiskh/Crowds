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
		/// Creates a mob entity with all the base components at the given position and returns its id.
		/// The position must already be snapped to the navmesh by the caller. Used by both regular spawning
		/// (MobSpawnSystem) and group spawning (GroupSpawnSystem), so initialization isn't duplicated.
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
			// Jitter: spread the first recalculation of a mob pack so not all are recalculated in one frame.
			pathRecalculationComponent.LastTime = now - Random.Range(0f, recalcInterval);

			looker.Transform = mob.ValueBar != null ? mob.ValueBar.Transform : null;
			looker.FlatBillboard = true;

			// Grenadier mob: the same mob + grenade-throw behavior (GrenadierSystem).
			if (mobConfig is GrenadierMobConfig grenadierConfig)
			{
				ref var grenadier = ref world.GetPool<GrenadierComponent>().Add(mobEntity);
				grenadier.Config = grenadierConfig;
				grenadier.State = GrenadierState.Chase;
				grenadier.Timer = 0f;
				grenadier.HasFleeTarget = false;
			}
			// Melee mob: the same mob + telegraphed attack (MeleeAttackerSystem).
			else if (mobConfig is MeleeMobConfig meleeMobConfig)
			{
				ref var attacker = ref world.GetPool<MeleeAttackerComponent>().Add(mobEntity);
				attacker.Config = meleeMobConfig;
				attacker.State = MeleeAttackerState.Chase;
				attacker.Timer = 0f;
			}
			// Ranged mob: the same mob + telegraphed shot (RangedAttackerSystem).
			else if (mobConfig is RangedMobConfig rangedMobConfig)
			{
				ref var ranged = ref world.GetPool<RangedAttackerComponent>().Add(mobEntity);
				ranged.Config = rangedMobConfig;
				ranged.State = RangedAttackerState.Chase;
				ranged.Timer = 0f;

				// Start in the config's chase clip (the generic default is Run, which a walking shooter may not have).
				ref var anim = ref world.GetPool<AnimationStateComponent>().Add(mobEntity);
				anim.Requested = rangedMobConfig.ChaseAnimation;
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
			// Spawned during the death cinematic: no health bar (GameOverActions.HideMobHealthBars).
			if (world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive)
				mob.ValueBar.SetVisible(false);
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
		/// Takes a mob by id from the pool stack or instantiates a new one.
		/// </summary>
		private static Mob SpawnMob(ref MobPoolComponent mobPool, MobConfig mobConfig)
		{
			if (mobPool.Pools == null)
				mobPool.Pools = new Dictionary<string, Stack<Mob>>();

			if (mobPool.Pools.TryGetValue(mobConfig.Id, out var stack) && stack.Count > 0)
				return stack.Pop();

			var mob = Object.Instantiate(mobConfig.Prefab, mobPool.Parent);
			SetupNewInstance(mob, mobConfig);
			return mob;
		}

		/// <summary>
		/// One-time per-config setup of a freshly instantiated mob (the pool is per config id, so pooled instances
		/// keep it). Shared by <see cref="SpawnMob"/> and the pool prewarm in EntryPoint — otherwise prewarmed
		/// instances would miss the weapon override / helmet.
		/// </summary>
		public static void SetupNewInstance(Mob mob, MobConfig mobConfig)
		{
			mob.SetId(mobConfig.Id);
			// Per-config gun on a shared model.
			if (mobConfig is RangedMobConfig ranged && ranged.WeaponOverride != null)
				mob.ReplaceWeapon(ranged.WeaponOverride);
			// Per-config helmet on the Head bone (fresh instance = bind pose, so the fit is clean). VAT mobs don't
			// animate their bones — a helmet would hang still, so they're skipped.
			if (mobConfig.Headwear != null)
			{
				if (mobConfig.CrowdLibrary == null)
					Headwear.AttachTo(mob.transform, mobConfig.Headwear);
				else
					Debug.LogWarning($"[MobSpawnSystem] '{mobConfig.Id}': Headwear is ignored for VAT crowd mobs (CrowdLibrary set).");
			}
		}

		private static void InitializeMobGameObject(Mob mob, MobConfig mobConfig, Vector2 playerPosition)
		{
			// A pooled mob may have died with a death animation (DamageSystem disables its collider then).
			if (mob.Collider != null)
				mob.Collider.enabled = true;

			mob.ValueBar.SetMaxValue(mobConfig.Health)
						.ApplyValue(mobConfig.Health)
						.SetVisible(true);

			mob.gameObject.SetActive(true);
		}
	}
}
