using Leopotam.EcsLite;
using System.Collections.Generic;
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
					continue;
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
				world.DelEntity(entity);
			}
			#endregion
		}

		/// <summary>
		/// Берёт декаль по id из стека пула или инстанцирует новую.
		/// </summary>
		private Decal SpawnDecal(DecalPoolComponent pool, DecalConfig config, Decal prefab)
		{
			Decal decal;
			if (pool.Pools != null && pool.Pools.TryGetValue(config.Id, out var stack) && stack.Count > 0)
			{
				decal = stack.Pop();
			}
			else
			{
				decal = Object.Instantiate(prefab, pool.Parent);
				decal.Initialize(config);
			}
			return decal;
		}

		private void DisposeDecal(EcsWorld world, int entity, DecalPoolComponent mainPool, DecalComponent decal)
		{
			decal.Value.Hide();
			if (mainPool.Pools == null)
				mainPool.Pools = new Dictionary<string, Stack<Decal>>();
			if (!mainPool.Pools.TryGetValue(decal.Value.Id, out var stack))
			{
				stack = new Stack<Decal>();
				mainPool.Pools[decal.Value.Id] = stack;
			}
			stack.Push(decal.Value);
			world.DelEntity(entity);
		}
	}
}