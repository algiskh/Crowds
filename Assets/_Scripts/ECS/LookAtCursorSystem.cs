using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public sealed class LookAtCursorSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var camera = ref world.GetAsSingleton<CameraComponent>().Value;
			ref var cursor = ref world.GetAsSingleton<VirtualAimCursorComponent>();

			var lookPool = world.GetPool<LookAtCursor>();

			Vector2 screenPos = cursor.ScreenPosition;

			// Луч из курсора — одинаковый для всех сущностей, считаем один раз.
			Ray cursorRay = camera.ScreenPointToRay(screenPos);

			foreach (var entity in world.Filter<LookAtCursor>().End())
			{
				ref var comp = ref lookPool.Get(entity);
				if (comp.Transform == null) continue;

				Vector3 lookTargetWorld;

				if (comp.Mode3D)
				{
					// Плоскость зависит от высоты цели, поэтому строим её индивидуально.
					var plane = new Plane(Vector3.up, comp.Transform.position);
					if (!plane.Raycast(cursorRay, out float distance))
						continue;
					lookTargetWorld = cursorRay.GetPoint(distance);
				}
				else
				{
					Vector3 objectScreenPos = camera.WorldToScreenPoint(comp.Transform.position);
					lookTargetWorld = camera.ScreenToWorldPoint(
						new Vector3(screenPos.x, screenPos.y, objectScreenPos.z));
				}

				Vector3 lookDir = lookTargetWorld - comp.Transform.position;
				if (lookDir.sqrMagnitude < 0.000001f) continue;
				lookDir.Normalize();

				if (comp.Mode3D)
				{
					comp.Transform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);
				}
				else
				{
					float angle = Mathf.Atan2(lookDir.x, lookDir.y) * Mathf.Rad2Deg;
					comp.Transform.rotation = Quaternion.Euler(0f, 0f, -angle);
				}
			}
		}
	}
}