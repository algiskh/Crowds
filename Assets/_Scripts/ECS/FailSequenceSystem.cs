using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Кинематографичная концовка при гибели игрока. Запускается из CheckEndSystem (ветка !isWin) и
	/// проводит три фазы по <see cref="PhaseDuration"/> (0.5с) каждая:
	///  1) BlockControls — блокирует только управление игроком (InputLockComponent); мир продолжает жить;
	///  2) RedScreen — плавно проявляет красную пелену поверх экрана;
	///  3) Menu — ставит игру на паузу (RequestPause), останавливает движение/аниматоры (GameOverActions)
	///     и показывает окно поражения (RequestOpenWindow → UISystem).
	///
	/// Работает на обычном Time.deltaTime: timeScale мы не трогаем, пауза реализована флагом
	/// PauseStateComponent, поэтому система продолжает тикать и доводит последовательность до конца.
	/// </summary>
	public sealed class FailSequenceSystem : IEcsInitSystem, IEcsRunSystem
	{
		public const float PhaseDuration = 0.5f;

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

			if (seq.Phase == FailSequencePhase.Inactive || seq.Phase == FailSequencePhase.Done)
				return;

			ref var overlay = ref world.GetAsSingleton<FailScreenOverlayComponent>();

			// Плавное проявление красной пелены в течение всей фазы RedScreen.
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
					// -> Красная пелена: создаём оверлей прозрачным и начинаем проявление.
					overlay.Value.EnsureCreated(RedColor);
					overlay.Value.SetAlpha(0f);
					seq.Phase = FailSequencePhase.RedScreen;
					seq.Timer = PhaseDuration;
					break;

				case FailSequencePhase.RedScreen:
					// -> Меню + полная остановка игры.
					overlay.Value.SetAlpha(RedAlpha);
					StopGameAndShowMenu(world);
					seq.Phase = FailSequencePhase.Done;
					break;
			}
		}

		private void StopGameAndShowMenu(EcsWorld world)
		{
			// Блок управления больше не нужен — дальше всё держит общая пауза.
			ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
			inputLock.Locked = false;

			ref var requestPause = ref world.CreateSimpleEntity<RequestPauseComponent>();
			requestPause.Source = SignalSource.EndGame;

			ref var requestOpenWindow = ref world.CreateSimpleEntity<RequestOpenWindowComponent>();
			requestOpenWindow.WindowType = WindowType.FailWindow;

			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			GameOverActions.StopAllMoves(world, player);
		}
	}

	/// <summary>
	/// Общие действия «полной остановки» при завершении игры — используются и победой (CheckEndSystem),
	/// и поражением (FailSequenceSystem): гасит движение всех сущностей, ставит аниматоры на паузу и
	/// блокирует спавн.
	/// </summary>
	public static class GameOverActions
	{
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

			// Мобы остаются живыми и переходят в Idle: AnimationSystem/CrowdRenderSystem не завязаны
			// на паузу и доиграют смену состояния (аниматоры не паузим).
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
