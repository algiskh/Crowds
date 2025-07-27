using Leopotam.EcsLite;
using UnityEngine;
namespace ECS
{
	public class ParallaxSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();

			ref var player = ref world.GetAsSingleton<PlayerComponent>();
			var parallaxPool = world.GetPool<ParallaxComponent>();
			var parallaxFilter = world.Filter<ParallaxComponent>().End();

			foreach(var entity in parallaxFilter)
			{
				if (!parallaxPool.Has(entity)) continue;
				var parallax = parallaxPool.Get(entity);
				var parallaxObject = parallax.Value;
				if (parallaxObject == null) continue;
				// Calculate the new position based on the player's position and the parallax factor
				var oldPos = parallaxObject.transform.position;
				var newPosition = new Vector2(player.Value.transform.position.x * parallaxObject.ParallaxFactor, oldPos.y);
				parallaxObject.transform.position = new Vector3(newPosition.x, newPosition.y, parallaxObject.transform.position.z);
			}
		}
	}
}