using Leopotam.EcsLite;
using System.Linq;
using UnityEngine;

namespace ECS
{
	public class DecalSystem : IEcsRunSystem
	{
		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var decalHolder = world.GetAsSingleton<DecalsHolderComponent>();
			var navmeshManager = world.GetAsSingleton<NavMeshManagerComponent>();
			ref var decalMainPool = ref world.GetAsSingleton<DecalPoolComponent>();
			var decalPool = world.GetPool<DecalComponent>();
			var currentSectorPool = world.GetPool<CurrentSectorComponent>();

			var lifetimePool = world.GetPool<LifeTimeComponent>();
			var disposablePool = world.GetPool<DisposableComponent>();

			var requetDecalPool = world.GetPool<RequestDecalComponent>();
			var filter = world.Filter<DecalComponent>().Inc<LifeTimeComponent>().Inc<DisposableComponent>().End();

			#region IteratingDecals
			foreach (var entity in filter)
			{
				ref var lt = ref lifetimePool.Get(entity);
				lt.Value -= Time.deltaTime;
			}
			#endregion

			#region HandlingDisposedDecal
			foreach (var entity in filter)
			{
				ref var lt = ref lifetimePool.Get(entity);
				ref var decal = ref decalPool.Get(entity);

				if (lt.Value <= 0)
				{
					DisposeDecal(world, entity, decalMainPool, decal);
					break;
				}

				ref var disposable = ref disposablePool.Get(entity);
				if (disposable.IsDisposed)
				{
					DisposeDecal(world, entity, decalMainPool, decal);
				}
			}
			#endregion

			#region CreatingEffects
			var requestFilter = world.Filter<RequestDecalComponent>().End();
			foreach (var entity in requestFilter)
			{
				Debug.Log($"Try to spawn decal");
				ref var request = ref requetDecalPool.Get(entity);

				var config = decalHolder.Value.GetConfig(request.Id);

				if (config == null)
				{
					Debug.LogWarning($"Couldn't find decal {request.Id} in EffectsHolder.");
					continue;
				}

				var decal = SpawnDecal(decalMainPool, config, decalHolder.Value.Prefab);

				decal.transform.position = request.Position;
				decal.transform.rotation = Quaternion.LookRotation(request.Direction, Vector3.up).TiltDown90();
				if (decal != null)
				{
					decal.Show();
					var newEntity = world.NewEntity();
					ref var effectComponent = ref decalPool.Add(newEntity);
					ref var lifetimeComponent = ref lifetimePool.Add(newEntity);
					ref var disposableComponent = ref disposablePool.Add(newEntity);

					effectComponent.Value = decal;
					lifetimeComponent.Value = config.LifeTime;
				}
				else
				{
					Debug.Log("Debag is null");
				}
				world.DelEntity(entity);
			}
			#endregion
		}

		/// <summary>
		/// Spawn new mob or take used mob from pool
		/// </summary>
		private Decal SpawnDecal(DecalPoolComponent pool, DecalConfig config, Decal prefab)
		{
			Decal effect;
			if (pool.Value != null &&
				pool.Value.Count > 0 &&
				pool.Value.Any(b => b.Id.Equals(config.Id)))
			{
				effect = pool.Value.First(mob => mob.Id.Equals(config.Id));
				pool.Value.Remove(effect);
			}
			else
			{
				effect = Object.Instantiate(
					prefab,
					pool.Parent);
				effect.Initialize(config);
			}
			return effect;
		}

		private void DisposeDecal(EcsWorld world, int entity, DecalPoolComponent mainPool, DecalComponent decal)
		{
			decal.Value.Hide();
			mainPool.Value.Add(decal.Value);
			world.DelEntity(entity);
		}
	}
}