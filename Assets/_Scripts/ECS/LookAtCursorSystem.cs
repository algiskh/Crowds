using Leopotam.EcsLite;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ECS
{
	public sealed class LookAtCursorSystem : IEcsRunSystem
	{
		private const float StickDeadZoneSqr = 0.01f;

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var camera = ref world.GetAsSingleton<CameraComponent>().Value;
			ref var cursor = ref world.GetAsSingleton<VirtualAimCursorComponent>();

			var lookPool = world.GetPool<LookAtCursor>();

			Vector2 screenPos = cursor.ScreenPosition;

			foreach (var entity in world.Filter<LookAtCursor>().End())
			{
				ref var comp = ref lookPool.Get(entity);

				if (comp.Transform == null)
					continue;

				Vector3 lookTargetWorld;

				if (comp.Mode3D)
				{
					Ray ray = camera.ScreenPointToRay(screenPos);

					Plane plane = new Plane(
						Vector3.up,
						comp.Transform.position);

					if (!plane.Raycast(ray, out float distance))
						continue;

					lookTargetWorld = ray.GetPoint(distance);
				}
				else
				{
					Vector3 objectScreenPos =
						camera.WorldToScreenPoint(comp.Transform.position);

					lookTargetWorld =
						camera.ScreenToWorldPoint(
							new Vector3(
								screenPos.x,
								screenPos.y,
								objectScreenPos.z));
				}

				Vector3 lookDir =
					lookTargetWorld - comp.Transform.position;

				if (lookDir.sqrMagnitude < 0.000001f)
					continue;

				lookDir.Normalize();

				if (comp.Mode3D)
				{
					comp.Transform.rotation =
						Quaternion.LookRotation(
							lookDir,
							Vector3.up);
				}
				else
				{
					float angle =
						Mathf.Atan2(lookDir.x, lookDir.y) *
						Mathf.Rad2Deg;

					comp.Transform.rotation =
						Quaternion.Euler(0f, 0f, -angle);
				}
			}
		}
	}
}
