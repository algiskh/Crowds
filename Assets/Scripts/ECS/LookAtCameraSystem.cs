using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class LookAtCameraSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var lookAtCameraPool = world.GetPool<LookAtCamera>();
			var cameraPool = world.GetPool<CameraComponent>();

			// Находим камеру
			Camera camera = null;
			foreach (var camEntity in world.Filter<CameraComponent>().End())
			{
				camera = cameraPool.Get(camEntity).Value;
				break;
			}
			if (camera == null) return;

			foreach (var entity in world.Filter<LookAtCamera>().End())
			{
				ref var comp = ref lookAtCameraPool.Get(entity);
				if (comp.Transform == null) continue;

				if (comp.FlatBillboard)
				{
					comp.Transform.rotation = Quaternion.LookRotation(
						camera.transform.forward, camera.transform.up);
				}
				else
				{
					comp.Transform.LookAt(camera.transform);
					// // Иногда Canvas надо инвертировать:
					// comp.Transform.forward = (comp.Transform.position - camera.transform.position).normalized;
				}
			}
		}
	}
}