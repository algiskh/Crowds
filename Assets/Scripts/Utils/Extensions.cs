using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

public static class Extensions
{
	public static T Copy<T>(this T source, Transform parent = null) where T : Component
	{
		if (source == null)
		{
			Debug.LogError("Null prefab");
			return null;
		}

		return source.gameObject.Copy(parent).GetComponent(source.GetType()) as T;
	}

	public static GameObject Copy(this GameObject source, Transform parent = null)
	{
		if (source == null)
		{
			Debug.LogError("Null prefab");
			return null;
		}
		var copy = UnityEngine.Object.Instantiate(source, parent);
		copy.name = source.name;
		copy.SetActive(true);
		foreach (var component in copy.GetComponents<Component>())
		{
			if (component is Transform) continue;
			if (component is RectTransform) continue;
			component.gameObject.SetActive(true);
		}
		return copy;
	}

	public static T GetRandomElement<T>(
		this IEnumerable<T> source,
		Func<T, bool> predicate = null,
		bool throwOnEmpty = true)
	{
		if (source == null)
		{
			if (throwOnEmpty)
				throw new InvalidOperationException("Source is null.");
			return default;
		}

		var list = source as IList<T> ?? source.ToList();
		if (list.Count == 0)
		{
			if (throwOnEmpty)
				throw new InvalidOperationException("Cannot select a random element from an empty collection.");
			return default;
		}

		var random = new System.Random();

		if (predicate == null)
		{
			return list[random.Next(list.Count)];
		}

		var indices = Enumerable.Range(0, list.Count)
								.OrderBy(_ => random.Next())
								.ToList();

		T lastTried = default!;
		foreach (var idx in indices)
		{
			var element = list[idx];
			lastTried = element;
			if (predicate(element))
			{
				return element;
			}
		}

		return lastTried;
	}

	public static IEnumerable<T> GetRandomUniqueElements<T>(this IEnumerable<T> source, int count, bool throwException = true)
	{
		if (source == null)
		{
			if (throwException)
				throw new InvalidOperationException("Cannot select random elements from a null collection");
			return Enumerable.Empty<T>();
		}

		var list = source.ToList();
		if (!list.Any())
		{
			if (throwException)
				throw new InvalidOperationException("Cannot select random elements from an empty collection");
			return Enumerable.Empty<T>();
		}

		if (count < 0)
			throw new ArgumentOutOfRangeException("Count cannot be negative");

		if (count > list.Count)
		{
			if (throwException)
				throw new InvalidOperationException($"Requested {count} elements but only {list.Count} available");
			count = list.Count;
		}

		var random = new System.Random();
		for (int i = 0; i < count; i++)
		{
			var j = random.Next(i, list.Count);

			T temp = list[i];
			list[i] = list[j];
			list[j] = temp;
		}

		return list.Take(count);
	}

	public static void Shuffle<T>(this IList<T> list, bool avoidOriginalNeighbors = true, int maxAttempts = 100)
	{
		int n = list.Count;

		if (!avoidOriginalNeighbors || n < 4)
		{
			RandomizeSimple(list);
			return;
		}

		var original = list.ToArray();

		for (int attempt = 0; attempt < maxAttempts; attempt++)
		{
			RandomizeSimple(list);
			if (!HasAdjacentOriginalNeighbors(list, original))
				return;
		}
	}

	private static void RandomizeSimple<T>(IList<T> list)
	{
		int n = list.Count;
		for (int i = 0; i < n - 1; i++)
		{
			int j = UnityEngine.Random.Range(i, n);
			T tmp = list[i];
			list[i] = list[j];
			list[j] = tmp;
		}
	}

	public static List<T> Multiply<T>(this IEnumerable<T> list, int times)
	{
		if (list == null || times <= 1)
			return new List<T>();
		var result = new List<T>(list.Count() * times);
		for (int i = 0; i < times; i++)
		{
			result.AddRange(list);
		}
		return result;
	}

	private static bool HasAdjacentOriginalNeighbors<T>(IList<T> shuffled, T[] original)
	{
		var indexMap = new Dictionary<T, int>(EqualityComparer<T>.Default);
		for (int i = 0; i < original.Length; i++)
			indexMap[original[i]] = i;

		for (int i = 0; i < shuffled.Count - 1; i++)
		{
			int idxA = indexMap[shuffled[i]];
			int idxB = indexMap[shuffled[i + 1]];
			if (Math.Abs(idxA - idxB) == 1)
				return true;
		}

		return false;
	}

	public static Quaternion TiltDown90(this Quaternion q)
	{
		return q * Quaternion.Euler(90f, 0f, 0f);
	}
}
