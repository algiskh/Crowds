using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Cinematic fail sequence on player death. Started from CheckEndSystem (the !isWin branch) and
	/// runs three phases of <see cref="PhaseDuration"/> (0.5s) each:
	///  1) BlockControls — locks only the player controls (InputLockComponent); the world keeps running;
	///  2) RedScreen — gradually fades a red veil in over the screen;
	///  3) Menu — shows the defeat window (RequestOpenWindow → UISystem) and pauses the game (RequestPause), stopping
	///     movement/animators (GameOverActions). Exception: while living friendly NPCs exist (FriendlyNpcComponent —
	///     the cheerleaders), the window opens over a LIVING world (no pause, spawns blocked, controls locked) so the
	///     escape and the chase play out behind it; the full stop comes <see cref="LastNpcGrace"/> s after the last
	///     one dies or turns.
	///
	/// Runs on the regular Time.deltaTime: we don't touch timeScale, pause is implemented via the
	/// PauseStateComponent flag, so the system keeps ticking and completes the sequence.
	/// </summary>
	public sealed class FailSequenceSystem : IEcsInitSystem, IEcsRunSystem
	{
		public const float PhaseDuration = 0.5f;
		private const float LastNpcGrace = 1.5f; // live world behind the window: full stop this long after the last friendly NPC

		private const float RedAlpha = 0.6f;
		private static readonly Color RedColor = new Color(0.7f, 0f, 0f, 1f);

		public void Init(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var seq = ref world.CreateSimpleEntity<FailSequenceComponent>();
			seq.Phase = FailSequencePhase.Inactive;
			seq.Timer = 0f;

			ref var inputLock = ref world.CreateSimpleEntity<InputLockComponent>();
			inputLock.Locked = false;

			ref var overlay = ref world.CreateSimpleEntity<FailScreenOverlayComponent>();
			overlay.Value = new FailScreenOverlay();
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			ref var seq = ref world.GetAsSingleton<FailSequenceComponent>();

			if (seq.Phase == FailSequencePhase.Inactive)
				return;
			if (seq.Phase == FailSequencePhase.Done)
			{
				// Fail window is up over a living world (friendly NPCs were alive): once none are left, give the last
				// death a moment, then do the usual full stop.
				if (seq.WorldLive && !HasFriendlyNpcs(world))
				{
					seq.Timer -= Time.deltaTime;
					if (seq.Timer <= 0f)
					{
						seq.WorldLive = false;
						FreezeWorld(world);
					}
				}
				return;
			}

			ref var overlay = ref world.GetAsSingleton<FailScreenOverlayComponent>();

			// Smooth fade-in of the red veil throughout the RedScreen phase.
			if (seq.Phase == FailSequencePhase.RedScreen)
			{
				float progress = Mathf.Clamp01(1f - seq.Timer / PhaseDuration);
				overlay.Value.SetAlpha(progress * RedAlpha);
			}

			seq.Timer -= Time.deltaTime;
			if (seq.Timer > 0f)
				return;

			switch (seq.Phase)
			{
				case FailSequencePhase.BlockControls:
					// -> Red veil: create the overlay transparent and start the fade-in.
					overlay.Value.EnsureCreated(RedColor);
					overlay.Value.SetAlpha(0f);
					seq.Phase = FailSequencePhase.RedScreen;
					seq.Timer = PhaseDuration;
					break;

				case FailSequencePhase.RedScreen:
					// -> Menu. Normally + full game stop; with living friendly NPCs (e.g. cheerleaders fleeing the
					// horde) the world keeps running behind the window until none are left.
					overlay.Value.SetAlpha(RedAlpha);
					ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
					requestOpenWindow.WindowType = WindowType.FailWindow;
					seq.Phase = FailSequencePhase.Done;
					if (HasFriendlyNpcs(world))
					{
						seq.WorldLive = true;
						seq.Timer = LastNpcGrace;
						// Controls stay locked (the player is dead); no new spawns, the ones on the field keep going.
						world.GetAsSingleton<SpawnRequestComponent>().IsBlocked = true;
					}
					else
					{
						FreezeWorld(world);
					}
					break;
			}
		}

		private static bool HasFriendlyNpcs(EcsWorld world) =>
			world.Filter<FriendlyNpcComponent>().End().GetEntitiesCount() > 0;

		// Full stop under the fail window: global pause, movement/animators stopped, spawns blocked.
		private static void FreezeWorld(EcsWorld world)
		{
			// The control lock is no longer needed — the global pause takes over from here.
			ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
			inputLock.Locked = false;

			ref var requestPause = ref world.CreateSimpleEntity<RequestPauseComponent>();
			requestPause.Source = SignalSource.EndGame;

			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			GameOverActions.StopAllMoves(world, player);
		}
	}

	/// <summary>
	/// Common "full stop" actions at game end — used by both victory (CheckEndSystem)
	/// and defeat (FailSequenceSystem): kills the movement of all entities, pauses the animators and
	/// blocks spawning.
	/// </summary>
	public static class GameOverActions
	{
		/// <summary>Player death: mob health bars are noise on the death cinematic and the fail menu.</summary>
		public static void HideMobHealthBars(EcsWorld world)
		{
			var mobPool = world.GetPool<MobComponent>();
			foreach (var entity in world.Filter<MobComponent>().End())
			{
				var mob = mobPool.Get(entity).Value;
				if (mob != null)
					mob.ValueBar?.SetVisible(false);
			}
		}

		public static void StopAllMoves(EcsWorld world, PlayerComponent player)
		{
			var movePool = world.GetPool<MoveComponent>();
			var animPool = world.GetPool<AnimationStateComponent>();

			foreach (var entity in world.Filter<MoveComponent>().End())
			{
				ref var move = ref movePool.Get(entity);
				move.Speed = 0f;
				move.Direction = Vector2.zero;
			}

			// Mobs stay alive and switch to Idle: AnimationSystem/CrowdRenderSystem aren't tied
			// to the pause and will finish the state change (we don't pause the animators).
			foreach (var entity in world.Filter<MobComponent>().End())
			{
				ref var anim = ref animPool.Has(entity) ? ref animPool.Get(entity) : ref animPool.Add(entity);
				anim.Requested = AnimationType.Idle;
			}

			player.Value.Animator.Pause();

			ref var spawnRequest = ref world.GetAsSingleton<SpawnRequestComponent>();
			spawnRequest.IsBlocked = true;
		}
	}
}
