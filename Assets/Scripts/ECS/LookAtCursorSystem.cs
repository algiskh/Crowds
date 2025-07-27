using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class LookAtCursorSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var lookAtCursorPool = world.GetPool<LookAtCursor>();
			var cameraPool = world.GetPool<CameraComponent>();

			ref var camera = ref world.GetAsSingleton<CameraComponent>().Value;

			foreach (var entity in world.Filter<LookAtCursor>().End())
			{
				ref var comp = ref lookAtCursorPool.Get(entity);
				if (comp.Transform == null) continue;

				Vector3 lookTargetWorld = Vector3.zero;

				// Пример для 3D — луч из курсора в плоскость
				if (comp.Mode3D)
				{
					Ray ray = camera.ScreenPointToRay(Input.mousePosition);
					// В какую плоскость смотреть? Например, Y = player.position.y
					var plane = new Plane(Vector3.up, comp.Transform.position);
					if (plane.Raycast(ray, out float dist))
					{
						lookTargetWorld = ray.GetPoint(dist);
					}
					else
					{
						continue;
					}
				}
				else
				{
					// 2D: Просто берем worldPoint под мышкой (z нужен как у трансформа)
					lookTargetWorld = camera.ScreenToWorldPoint(
						new Vector3(Input.mousePosition.x, Input.mousePosition.y, camera.WorldToScreenPoint(comp.Transform.position).z)
					);
				}

				// --- Поворот ---
				Vector3 lookDir = (lookTargetWorld - comp.Transform.position).normalized;
				Quaternion rot;
				if (comp.Mode3D)
				{
					rot = Quaternion.LookRotation(lookDir, Vector3.up);
				}
				else
				{
					// 2D: только по z-оси (например, top-down)
					float angle = Mathf.Atan2(lookDir.x, lookDir.y) * Mathf.Rad2Deg;
					rot = Quaternion.Euler(0, 0, -angle);
				}
				comp.Transform.rotation = rot;
			}
		}
	}
}
