using Leopotam.EcsLite;
using UnityEngine;

namespace ECS
{
	public class LookAtCameraSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var lookAtCameraPool = world.GetPool<LookerAtCamera>();

			if (!world.TryGetAsSingleton<CameraComponent>(out var camComp) || camComp.Value == null)
				return;

			var cameraTransform = camComp.Value.transform;
			var cameraForward = cameraTransform.forward;
			var cameraUp = cameraTransform.up;
			var cameraPos = cameraTransform.position;

			// Вычисляем «flat billboard»-кватернион один раз за кадр.
			var flatRotation = Quaternion.LookRotation(cameraForward, cameraUp);

			foreach (var entity in world.Filter<LookerAtCamera>().End())
			{
				ref var comp = ref lookAtCameraPool.Get(entity);
				if (comp.Transform == null || !comp.Transform.gameObject.activeSelf) continue;

				if (comp.FlatBillboard)
				{
					comp.Transform.rotation = flatRotation;
				}
				else
				{
					comp.Transform.LookAt(cameraPos);
				}
			}
		}
	}
}