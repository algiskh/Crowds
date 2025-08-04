using Leopotam.EcsLite;

namespace ECS
{
	public static class Extensions
	{
		public delegate void RefEntityAction<T>(ref T item) where T : struct;

		public static ref T GetAsSingleton<T>(this EcsWorld world) where T : struct
		{
			var pool = world.GetPool<T>();
			var filter = world.Filter<T>().End();

			var entities = filter.GetRawEntities();
			var count = filter.GetEntitiesCount();
			if (count == 0)
				throw new System.InvalidOperationException($"No singleton component of type {typeof(T).Name} found!");

			return ref pool.Get(entities[0]);
		}

		public static bool TryGetAsSingleton<T>(this EcsWorld world, out T value) where T : struct
		{
			var pool = world.GetPool<T>();
			var filter = world.Filter<T>().End();

			var entities = filter.GetRawEntities();
			var count = filter.GetEntitiesCount();
			if (count > 0)
			{
				value = pool.Get(entities[0]);
				return true;
			}
			value = default;
			return false;
		}

		public static ref T1 GetAsSingleton<T1, T2>(this EcsWorld world)
	where T1 : struct
	where T2 : struct
		{
			var pool = world.GetPool<T1>();
			var filter = world.Filter<T1>().Inc<T2>().End();

			var entities = filter.GetRawEntities();
			var count = filter.GetEntitiesCount();
			if (count == 0)
				throw new System.InvalidOperationException(
					$"No singleton entity with components {typeof(T1).Name} & {typeof(T2).Name} found!");

			return ref pool.Get(entities[0]);
		}

		public static bool TryGetAsSingleton<T1, T2>(this EcsWorld world, out T1 value)
			where T1 : struct
			where T2 : struct
		{
			var pool = world.GetPool<T1>();
			var filter = world.Filter<T1>().Inc<T2>().End();

			var entities = filter.GetRawEntities();
			var count = filter.GetEntitiesCount();
			if (count > 0)
			{
				value = pool.Get(entities[0]);
				return true;
			}
			value = default;
			return false;
		}

		public static void ForEachComponentInTheWorld<T>(this EcsWorld world, RefEntityAction<T> action, bool deleteAfter = false) where T : struct
		{
			var pool = world.GetPool<T>();
			var filter = world.Filter<T>().End();

			foreach (var entity in filter)
			{
				ref T component = ref pool.Get(entity);
				action(ref component);
			}

			if (deleteAfter)
			{
				foreach (var entity in filter)
				{
					world.DelEntity(entity);
				}
			}
		}

		public static void ForEachComponent<T>(this EcsFilter filter, EcsPool<T> pool, RefEntityAction<T> action) where T : struct
		{
			foreach (var entity in filter)
			{
				ref T component = ref pool.Get(entity);
				action(ref component);
			}
		}

		public static T[] GetAllComponentsAsCopies<T>(this EcsWorld world) where T : struct
		{
			var pool = world.GetPool<T>();
			var filter = world.Filter<T>().End();

			var entities = filter.GetRawEntities();
			int count = filter.GetEntitiesCount();
			var result = new T[count];

			for (int i = 0; i < count; i++)
			{
				result[i] = pool.Get(entities[i]);
			}
			return result;
		}

		public static void DeleteAllWith<T>(this EcsWorld world) where T : struct
		{
			var filter = world.Filter<T>().End();
			foreach (var entity in filter)
			{
				world.DelEntity(entity);
			}
		}

		public static ref T CreateSimpleEntity<T>(this EcsWorld world) where T : struct
		{
			var pool = world.GetPool<T>();
			var entity = world.NewEntity();
			return ref pool.Add(entity);
		}
	}
}