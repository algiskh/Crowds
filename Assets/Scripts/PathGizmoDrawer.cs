using Leopotam.EcsLite;
using UnityEngine;
using ECS;


public class PathGizmoDrawer : MonoBehaviour
{
	[SerializeField] private Color PathColor = Color.cyan;
	[SerializeField] private float SphereRadius = 0.15f;

	private EcsWorld _world;

	public void Initialize(EcsWorld ecsWorld)
	{
		_world = ecsWorld;
	}

	void OnDrawGizmos()
	{
		if (_world == null)
			return;

		var mobPool = _world.GetPool<MobComponent>();
		var pathPool = _world.GetPool<MovePath>();

		var filter = _world.Filter<MobComponent>().Inc<MovePath>().End();

		Gizmos.color = PathColor;

		foreach (var entity in filter)
		{
			ref var mob = ref mobPool.Get(entity);
			ref var path = ref pathPool.Get(entity);

			if (path.Waypoints == null || path.Waypoints.Count < 2)
				continue;

			foreach (var point in path.Waypoints)
				Gizmos.DrawSphere(point, SphereRadius);

			for (int i = 0; i < path.Waypoints.Count - 1; i++)
				Gizmos.DrawLine(path.Waypoints[i], path.Waypoints[i + 1]);
		}
	}
}
