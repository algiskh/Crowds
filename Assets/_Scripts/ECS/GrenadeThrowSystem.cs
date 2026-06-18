using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	/// <summary>
	/// Читает удержание действия "Throw": чем дольше зажато — тем дальше бросок.
	/// Дальность интерполируется между Min/MaxThrowDistance из PlayerConfig (механика игрока),
	/// а скорость/урон/радиус/эффекты берутся из текущего GrenadeConfig (тип гранаты).
	/// Направление берётся от игрока к точке прицела (под курсором/стиком) — тем же
	/// приёмом, что и прицеливание оружия (луч из курсора в плоскость на высоте игрока).
	/// Во время зарядки показывает предполагаемую точку попадания (GrenadeAimVisualizer).
	/// На отпускании тратит гранату и бросает снаряд по дуге (GrenadeProjectileComponent),
	/// при этом, если у конфига есть трейл-эффект, он делается ребёнком гранаты и летит с ней.
	/// </summary>
	public sealed class GrenadeThrowSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var pauseState = ref world.GetAsSingleton<PauseStateComponent>();
			ref var inputLock = ref world.GetAsSingleton<InputLockComponent>();
			if (pauseState.IsPaused || inputLock.Locked)
				return;

			ref var inputActions = ref world.GetAsSingleton<InputActionsComponent>();
			var throwAction = inputActions.ThrowAction;
			if (throwAction == null)
				return;

			ref var grenade = ref world.GetAsSingleton<GrenadeStateComponent>();
			ref var mainHolder = ref world.GetAsSingleton<MainHolderComponent>();
			var config = mainHolder.Value.PlayerConfig;
			var grenadeConfig = grenade.CurrentConfig;

			GrenadeAimVisualizer visualizer = null;
			if (world.TryGetAsSingleton<GrenadeAimVisualizerComponent>(out var vis))
				visualizer = vis.Value;

			bool isHeld = throwAction.ReadValue<float>() > 0.5f;
			bool canThrow = grenade.Count > 0 && grenadeConfig != null;

			if (isHeld)
			{
				grenade.IsCharging = true;
				grenade.ChargeTime += Time.deltaTime;

				// Превью точки попадания — только когда есть что и чем бросать.
				if (canThrow)
				{
					float ratio = ChargeRatio(config, grenade.ChargeTime);
					Vector3 preview = ComputeLandingPosition(world, config, ratio);
					if (visualizer != null)
						visualizer.Show(preview, grenadeConfig.Radius);
				}
				else if (visualizer != null)
				{
					visualizer.Hide();
				}
				return;
			}

			// Кнопку не держат. Если в прошлом кадре заряжались — это отпускание = бросок.
			if (!grenade.IsCharging)
				return;

			float chargeRatio = ChargeRatio(config, grenade.ChargeTime);
			grenade.IsCharging = false;
			grenade.ChargeTime = 0f;

			if (visualizer != null)
				visualizer.Hide();

			if (!canThrow)
				return;

			Vector3 landing = ComputeLandingPosition(world, config, chargeRatio);

			grenade.Count--;
			world.CreateSimpleEntity<UpdateGrenadeViewRequestComponent>();

			SpawnProjectile(world, mainHolder.Value, grenadeConfig, landing);
		}

		private static float ChargeRatio(PlayerConfig config, float chargeTime)
			=> config.MaxThrowChargeTime > 0f
				? Mathf.Clamp01(chargeTime / config.MaxThrowChargeTime)
				: 1f;

		/// <summary>
		/// Создаёт летящий снаряд от игрока к точке приземления через общий GrenadeLauncher.
		/// Кого задевает взрыв и насколько — задаётся долями урона в GrenadeConfig.
		/// </summary>
		private void SpawnProjectile(EcsWorld world, MainHolder mainHolder, GrenadeConfig grenadeConfig, Vector3 landing)
		{
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			Vector3 start = player.Value.transform.position;

			GrenadeLauncher.Launch(world, mainHolder, grenadeConfig, start, landing);
		}

		/// <summary>
		/// Точка приземления = позиция игрока + направление прицела * дальность(заряд).
		/// Направление считается через луч из курсора в плоскость на высоте игрока
		/// (тот же приём, что в AimVisualizer / LookAtCursorSystem).
		/// </summary>
		private Vector3 ComputeLandingPosition(EcsWorld world, PlayerConfig config, float chargeRatio)
		{
			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			Vector3 origin = player.Value.transform.position;
			Vector3 aimDir = player.Value.transform.forward;

			if (world.TryGetAsSingleton<CameraComponent>(out var cam) && cam.Value != null &&
				world.TryGetAsSingleton<VirtualAimCursorComponent>(out var cursor))
			{
				Ray ray = cam.Value.ScreenPointToRay(cursor.ScreenPosition);
				var plane = new Plane(Vector3.up, origin);
				if (plane.Raycast(ray, out float enter))
				{
					Vector3 aimWorld = ray.GetPoint(enter);
					Vector3 flat = aimWorld - origin;
					flat.y = 0f;
					if (flat.sqrMagnitude > 0.0001f)
						aimDir = flat.normalized;
				}
			}

			float distance = Mathf.Lerp(config.MinThrowDistance, config.MaxThrowDistance, chargeRatio);

			Vector3 landing = origin + aimDir * distance;
			landing.y = origin.y;
			return landing;
		}
	}
}
