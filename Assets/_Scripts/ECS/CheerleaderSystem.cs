using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.AI;

namespace ECS
{
	/// <summary>
	/// Cheerleader NPCs on a <see cref="CheerleaderStand"/> (hockey level) and the hidden "don't forget the girls"
	/// quest. Per girl (<see cref="CheerleaderComponent"/>):
	///  • Dancing — a random dance from her set; when she leaves the screen (bounds vs camera frustum) she swaps to
	///    another dance, so the player sees a new one next time.
	///  • Forgotten — checked only on the frame a breather (<see cref="CheerleaderStand.IsTransformStage"/>) starts: if
	///    none of the girls is on screen at that moment they vanish, zombie cheerleaders
	///    (<see cref="CheerleaderStand.ZombieMobId"/>) spawn at the floor points under the stage and the UI log says the
	///    hint. Quest → Forgotten. Seen at the transition → they stay alive for the whole breather.
	///  • Victory (stage 'finish') — each girl takes her own static pose. Quest → Saved (if never forgotten).
	///  • Player death — they jump down to the floor in front of the stage and run (NavMeshAgent) to the far corner;
	///    they get a <see cref="ZombieTargetComponent"/>, so MobPathfindingSystem sends the horde after them. Mob contact
	///    (MobConfig.HitRadius/Damage/HitCooldown) hurts them; 50 HP → death animation.
	/// Not targets for the player (no collider in the damage maps). Frozen while the game is paused.
	/// See Docs/CheerleaderFeature.md.
	/// </summary>
	public sealed class CheerleaderSystem : IEcsInitSystem, IEcsRunSystem
	{
		private const float ContactSlack = 0.3f;      // extra reach on top of MobConfig.HitRadius (girls are slim)
		private const float FloorSampleRadius = 3f;
		private const float EscapeSpread = 2f;

		private readonly Plane[] _frustum = new Plane[6];
		private bool _frozen;
		private bool _hasLastStage;
		private DifficultyLevel _lastStage;
		// Per stand: was any dancing girl on screen last frame (drives the synced dance swap).
		private readonly System.Collections.Generic.Dictionary<CheerleaderStand, bool> _standVisible = new();
		private readonly System.Collections.Generic.Dictionary<CheerleaderStand, bool> _groupScratch = new();

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var stands = Object.FindObjectsByType<CheerleaderStand>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			if (stands.Length == 0)
				return;

			world.CreateSimpleEntity<CheerleaderQuestComponent>();
			var pool = world.GetPool<CheerleaderComponent>();
			var friendlyPool = world.GetPool<FriendlyNpcComponent>();
			foreach (var stand in stands)
			{
				var girls = stand.Cheerleaders;
				if (girls == null)
					continue;
				// One dance for the whole stand, same phase → they dance in sync.
				string dance = null;
				foreach (var g in girls)
					if (g != null) { dance = RandomOf(g.DanceStates, null); break; }
				float phase = Random.value;
				_standVisible[stand] = true;
				for (int i = 0; i < girls.Length; i++)
				{
					var view = girls[i];
					if (view == null)
						continue;

					var slots = stand.Slots;
					if (slots != null && i < slots.Length && slots[i] != null)
						view.transform.SetPositionAndRotation(slots[i].position, slots[i].rotation);
					if (view.Agent != null)
						view.Agent.enabled = false; // the deck is off the navmesh
					view.gameObject.SetActive(true);
					view.PlayAtPhase(dance, phase);
					view.SetPropsVisible(true);

					int entity = world.NewEntity();
					friendlyPool.Add(entity);
					ref var girl = ref pool.Add(entity);
					girl.View = view;
					girl.Stand = stand;
					girl.Index = i;
					girl.State = CheerleaderState.Dancing;
					girl.Health = view.MaxHealth;
					girl.WasInView = true;
				}
			}
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var filter = world.Filter<CheerleaderComponent>().End();
			if (filter.GetEntitiesCount() == 0)
				return;
			var pool = world.GetPool<CheerleaderComponent>();

			// Pause / fail menu: freeze animators and agents (they live outside ECS movement).
			bool paused = world.TryGetAsSingleton<PauseStateComponent>(out var pause) && pause.IsPaused;
			if (paused != _frozen)
			{
				_frozen = paused;
				foreach (var e in filter)
				{
					ref var g = ref pool.Get(e);
					// Victory poses keep blending in behind the win window (the pause comes the next frame).
					if (g.View != null && g.View.gameObject.activeInHierarchy && g.State != CheerleaderState.Posing)
						g.View.SetFrozen(paused);
				}
			}
			if (paused)
				return;

			bool playerDown = world.TryGetAsSingleton<FailSequenceComponent>(out var fail) && fail.Phase != FailSequencePhase.Inactive;
			bool hasStage = world.TryGetAsSingleton<DifficultyComponent>(out var difficulty) && difficulty.Stage != null;
			DifficultyLevel stage = hasStage ? difficulty.Stage.DifficultyLevel : default;
			bool victory = hasStage && stage == DifficultyLevel.finish && !playerDown;

			var cam = Camera.main;
			bool hasCam = cam != null;
			if (hasCam)
				GeometryUtility.CalculateFrustumPlanes(cam, _frustum);

			ref var quest = ref world.GetAsSingleton<CheerleaderQuestComponent>();

			// --- Forgotten: checked ONLY on the frame a breather starts (wave → breather). If none of the girls is on
			// screen at that moment → zombie swap; otherwise they stay alive for the whole breather.
			bool stageStarted = hasStage && _hasLastStage && stage != _lastStage;
			if (hasStage)
			{
				_hasLastStage = true;
				_lastStage = stage;
			}
			if (stageStarted && !playerDown && hasCam && !quest.Forgotten)
				TryTransform(world, filter, pool, stage, ref quest);

			foreach (var entity in filter)
			{
				ref var girl = ref pool.Get(entity);
				var view = girl.View;
				if (view == null)
					continue;

				switch (girl.State)
				{
					case CheerleaderState.Dancing:
						if (playerDown)
						{
							StartEscape(world, entity, ref girl);
							break;
						}
						if (victory)
						{
							StrikePose(ref girl);
							if (!quest.Forgotten)
								quest.Saved = true;
							break;
						}
						girl.WasInView = hasCam && view.IsInView(_frustum);
						break;

					case CheerleaderState.Posing:
						// Victory pause stops everything anyway; nothing to do.
						break;

					case CheerleaderState.Escaping:
					case CheerleaderState.Safe:
						TakeMobContact(world, entity, ref girl);
						if (girl.State == CheerleaderState.Escaping && HasArrived(view.Agent))
						{
							girl.State = CheerleaderState.Safe;
							view.Play(view.IdleState);
						}
						break;
				}
			}

			SwapDanceWhenHidden(filter, pool);
		}

		// ------------------------------------------------------------------ synced dance

		/// <summary>
		/// The girls of a stand share one dance. When the whole group leaves the screen (all dancing girls off the
		/// frustum after at least one was visible), they all switch to another dance at a shared random phase.
		/// </summary>
		private void SwapDanceWhenHidden(EcsFilter filter, EcsPool<CheerleaderComponent> pool)
		{
			_groupScratch.Clear();
			foreach (var entity in filter)
			{
				ref var girl = ref pool.Get(entity);
				if (girl.State != CheerleaderState.Dancing || girl.Stand == null || girl.View == null)
					continue;
				_groupScratch.TryGetValue(girl.Stand, out bool anyVisible);
				_groupScratch[girl.Stand] = anyVisible || girl.WasInView;
			}

			foreach (var pair in _groupScratch)
			{
				var stand = pair.Key;
				bool visible = pair.Value;
				_standVisible.TryGetValue(stand, out bool wasVisible);
				_standVisible[stand] = visible;
				if (!wasVisible || visible)
					continue;

				string dance = null, current = null;
				float phase = Random.value;
				foreach (var entity in filter)
				{
					ref var girl = ref pool.Get(entity);
					if (girl.State != CheerleaderState.Dancing || girl.Stand != stand || girl.View == null)
						continue;
					current ??= girl.View.CurrentState;
					dance ??= RandomOf(girl.View.DanceStates, current);
					girl.View.PlayAtPhase(dance, phase);
				}
			}
		}

		// ------------------------------------------------------------------ forgotten / zombie swap

		// Called on the first frame of a new stage: if it's a breather and no girl is on screen right now → zombies.
		private void TryTransform(EcsWorld world, EcsFilter filter, EcsPool<CheerleaderComponent> pool,
			DifficultyLevel stage, ref CheerleaderQuestComponent quest)
		{
			CheerleaderStand stand = null;
			foreach (var entity in filter)
			{
				ref var girl = ref pool.Get(entity);
				if (girl.State != CheerleaderState.Dancing || girl.Stand == null || girl.View == null || !girl.Stand.IsTransformStage(stage))
					return;
				if (girl.View.IsInView(_frustum))
					return; // the player sees at least one of them at the transition → they stay alive this breather
				stand = girl.Stand;
			}
			if (stand == null)
				return;

			foreach (var entity in filter)
			{
				ref var girl = ref pool.Get(entity);
				girl.State = CheerleaderState.Transformed;
				UnmarkFriendly(world, entity);
				if (girl.View != null)
					girl.View.gameObject.SetActive(false);
			}

			var config = world.TryGetAsSingleton<MainHolderComponent>(out var main) && main.Value != null
				? main.Value.MobConfigHolder.GetConfigById(stand.ZombieMobId)
				: null;
			if (config == null)
				Debug.LogWarning($"[CheerleaderSystem] Mob config '{stand.ZombieMobId}' not found — no zombie cheerleaders spawned.");
			else if (stand.FloorPoints != null)
				foreach (var point in stand.FloorPoints)
				{
					if (point == null)
						continue;
					var pos = point.position;
					if (NavMesh.SamplePosition(pos, out var hit, FloorSampleRadius, NavMesh.AllAreas))
						pos = hit.position;
					MobSpawnSystem.CreateMob(world, config, pos);
				}

			if (!string.IsNullOrEmpty(stand.ForgottenHint))
			{
				ref var log = ref world.CreateSimpleEntity<RequestUILogComponent>();
				log.Message = stand.ForgottenHint;
			}
			quest.Forgotten = true;
			quest.Saved = false;
		}

		// ------------------------------------------------------------------ victory

		private static void StrikePose(ref CheerleaderComponent girl)
		{
			var poses = girl.View.PoseStates;
			girl.State = CheerleaderState.Posing;
			girl.View.SetPropsVisible(false);
			if (poses == null || poses.Length == 0)
			{
				girl.View.Play(girl.View.IdleState);
				return;
			}
			// Each girl her own pose: by slot index (distinct while there are enough poses).
			girl.View.Play(poses[girl.Index % poses.Length]);
		}

		// ------------------------------------------------------------------ escape on player death

		private static void StartEscape(EcsWorld world, int entity, ref CheerleaderComponent girl)
		{
			var view = girl.View;
			var stand = girl.Stand;
			var agent = view.Agent;
			girl.State = CheerleaderState.Escaping;
			view.SetPropsVisible(false);

			// Jump down: onto the floor in front of the stage (the deck itself is Not Walkable).
			Vector3 start = view.transform.position;
			var points = stand != null ? stand.FloorPoints : null;
			if (points != null && points.Length > 0 && points[girl.Index % points.Length] != null)
				start = points[girl.Index % points.Length].position;
			if (!NavMesh.SamplePosition(start, out var hit, FloorSampleRadius, NavMesh.AllAreas) || agent == null)
			{
				// No navmesh around — she just stays and cowers, still a target.
				view.Play(view.IdleState);
				girl.State = CheerleaderState.Safe;
				world.GetPool<ZombieTargetComponent>().Add(entity).Value = view.transform;
				return;
			}

			view.transform.position = hit.position;
			agent.enabled = true;
			int mobOnlyArea = NavMesh.GetAreaFromName("MobOnly");
			if (mobOnlyArea >= 0)
				agent.areaMask &= ~(1 << mobOnlyArea); // never flee into the mob tunnels
			agent.Warp(hit.position);
			agent.speed = view.RunSpeed;
			agent.isStopped = false;

			Vector3 goal = stand != null && stand.EscapePoint != null ? stand.EscapePoint.position : hit.position;
			goal += new Vector3(Random.Range(-EscapeSpread, EscapeSpread), 0f, Random.Range(-EscapeSpread, EscapeSpread));
			if (NavMesh.SamplePosition(goal, out var goalHit, FloorSampleRadius * 2f, NavMesh.AllAreas))
				goal = goalHit.position;
			agent.SetDestination(goal);
			view.Play(view.RunState);

			world.GetPool<ZombieTargetComponent>().Add(entity).Value = view.transform;
		}

		private static bool HasArrived(NavMeshAgent agent) =>
			agent == null || !agent.enabled || (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.3f);

		// Mob contact damage, the same numbers mobs use against the player (CollisionSystem).
		private static void TakeMobContact(EcsWorld world, int entity, ref CheerleaderComponent girl)
		{
			if (girl.HitCooldown > 0f)
			{
				girl.HitCooldown -= Time.deltaTime;
				return;
			}

			var mobPool = world.GetPool<MobComponent>();
			Vector3 pos = girl.View.transform.position;
			foreach (var mobEntity in world.Filter<MobComponent>().End())
			{
				ref var mob = ref mobPool.Get(mobEntity);
				if (mob.Value == null || mob.Config == null || !mob.Value.gameObject.activeInHierarchy)
					continue;
				Vector3 d = mob.Value.transform.position - pos;
				d.y = 0f;
				float reach = mob.Config.HitRadius + ContactSlack;
				if (d.sqrMagnitude > reach * reach)
					continue;

				girl.Health -= mob.Config.Damage;
				girl.HitCooldown = Mathf.Max(0.2f, mob.Config.HitCooldown);
				if (girl.Health <= 0f)
					Die(world, entity, ref girl);
				return;
			}
		}

		private static void Die(EcsWorld world, int entity, ref CheerleaderComponent girl)
		{
			girl.State = CheerleaderState.Dead;
			UnmarkFriendly(world, entity);
			var agent = girl.View.Agent;
			if (agent != null && agent.enabled)
			{
				if (agent.isOnNavMesh)
					agent.isStopped = true;
				agent.enabled = false;
			}
			girl.View.Play(girl.View.DeathState);
			var targets = world.GetPool<ZombieTargetComponent>();
			if (targets.Has(entity))
				targets.Del(entity);
		}

		// No longer a living friendly NPC (FailSequence uses the count to decide how long the death scene runs).
		private static void UnmarkFriendly(EcsWorld world, int entity)
		{
			var friendly = world.GetPool<FriendlyNpcComponent>();
			if (friendly.Has(entity))
				friendly.Del(entity);
		}

		private static string RandomOf(string[] states, string except)
		{
			if (states == null || states.Length == 0)
				return null;
			if (states.Length == 1)
				return states[0];
			string pick;
			int guard = 0;
			do pick = states[Random.Range(0, states.Length)];
			while (pick == except && ++guard < 8);
			return pick;
		}
	}
}
