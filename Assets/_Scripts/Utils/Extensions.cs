using System.Collections.Generic;
using System;
using UnityEngine;
using Random = System.Random;
using Unity.VisualScripting;

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
	this IList<T> list,
	Func<T, bool> predicate = null,
	bool throwOnEmpty = true)
	{
		if (list == null || list.Count == 0)
		{
			if (throwOnEmpty)
				throw new InvalidOperationException("Empty collection");
			return default;
		}

		if (predicate == null)
		{
			return list[UnityEngine.Random.Range(0, list.Count)];
		}

		T result = default;
		int validCount = 0;

		for (int i = 0; i < list.Count; i++)
		{
			var item = list[i];

			if (!predicate(item))
				continue;

			validCount++;

			if (UnityEngine.Random.Range(0, validCount) == 0)
			{
				result = item;
			}
		}

		if (validCount == 0 && throwOnEmpty)
			throw new InvalidOperationException("No matching elements");

		return result;
	}


	public static T GetRandomByWeight<T>(this IEnumerable<T> collection) where T : IWeightable
	{
		var list = new List<T>();
		foreach (var item in collection)
		{
			list.Add(item);
		}

		return GetRandomByWeightFromList(list);
	}

	public static T GetRandomByWeightFromList<T>(
		this IList<T> list,
		Func<T, bool> predicate = null,
		bool throwOnEmpty = false)
		where T : IWeightable
	{
		if (list == null || list.Count == 0)
		{
			if (throwOnEmpty)
				throw new InvalidOperationException("Empty collection");
			return default;
		}

		float totalWeight = 0f;
		T selected = default;

		for (int i = 0; i < list.Count; i++)
		{
			var item = list[i];

			if (predicate != null && !predicate(item))
				continue;

			float weight = item.Weight;
			if (weight <= 0f)
				continue;

			totalWeight += weight;

			if (UnityEngine.Random.value * totalWeight < weight)
			{
				selected = item;
			}
		}

		if (selected == null && throwOnEmpty)
			throw new InvalidOperationException("No valid elements");

		return selected;
	}


	public static IEnumerable<T> GetRandomUniqueElementsFast<T>(
		this IList<T> source,
		int count,
		bool throwException = true)
	{
		if (source == null)
		{
			if (throwException)
				throw new InvalidOperationException("Cannot select random elements from a null collection");
			return null;
		}

		int sourceCount = source.Count;

		if (sourceCount == 0)
		{
			if (throwException)
				throw new InvalidOperationException("Cannot select random elements from an empty collection");
			return null;
		}

		if (count < 0)
			throw new ArgumentOutOfRangeException(nameof(count), "Count cannot be negative");

		if (count > sourceCount)
		{
			if (throwException)
				throw new InvalidOperationException($"Requested {count} elements but only {sourceCount} available");
			count = sourceCount;
		}

		if (count == sourceCount)
			return source; // Просто возвращаем исходный список

		var random = new Random();
		var result = new T[count];

		// Копируем первые count элементов во временное хранилище
		// и одновременно перемешиваем
		for (int i = 0; i < count; i++)
		{
			int j = random.Next(i, sourceCount);

			// Меняем местами в исходной коллекции (если это допустимо)
			if (j != i)
			{
				T temp = source[i];
				source[i] = source[j];
				source[j] = temp;
			}

			result[i] = source[i];
		}

		// Восстанавливаем исходный порядок (если нужно)
		// for (int i = count - 1; i >= 0; i--) { ... }

		return result;
	}

	public static void Shuffle<T>(this IList<T> list, bool avoidOriginalNeighbors = true, int maxAttempts = 100)
	{
		int n = list.Count;

		if (!avoidOriginalNeighbors || n < 4)
		{
			RandomizeSimple(list);
			return;
		}

		var original = new T[n];
		for (int i = 0; i < n; i++)
			original[i] = list[i];

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

	public static List<T> Multiply<T>(this IList<T> list, int times)
	{
		if (list == null || times <= 1)
			return new List<T>();
		var result = new List<T>(list.Count * times);
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

	public static float DistanceTo(this Vector3 a, Vector3 b)
	{
		return Vector3.Distance(a, b);
	}

	public static float DistanceTo(this Component a, Component b)
	{
		return Vector3.Distance(a.transform.position, b.transform.position);
	}

	public static bool IsWithinXZBoundsFromMeshes(this Component target, FloorSector floorSector, float offsetZ = 0f)
	{
		return IsWithinXZBoundsFromMeshes(target.transform.position, floorSector, offsetZ);
	}

	public static bool IsWithinXZBoundsFromMeshes(this Vector3 targetPos, FloorSector floorSector, float offsetZ = 0f)
	{
		var meshFilters = floorSector.MeshFilters;

		if (meshFilters.Length == 0)
		{
			Debug.LogWarning("Нет MeshFilter у объекта или его дочерних элементов!");
			return false;
		}

		var combinedBounds = meshFilters[0].mesh.bounds;
		var worldMatrix = meshFilters[0].transform.localToWorldMatrix;
		var min = worldMatrix.MultiplyPoint3x4(combinedBounds.min);
		var max = worldMatrix.MultiplyPoint3x4(combinedBounds.max);

		float minX = min.x;
		float maxX = max.x;
		float minZ = min.z;
		float maxZ = max.z;

		for (int i = 1; i < meshFilters.Length; i++)
		{
			var mesh = meshFilters[i].mesh;
			var matrix = meshFilters[i].transform.localToWorldMatrix;

			var boundsMin = matrix.MultiplyPoint3x4(mesh.bounds.min);
			var boundsMax = matrix.MultiplyPoint3x4(mesh.bounds.max);

			minX = Mathf.Min(minX, boundsMin.x);
			maxX = Mathf.Max(maxX, boundsMax.x);
			minZ = Mathf.Min(minZ, boundsMin.z);
			maxZ = Mathf.Max(maxZ, boundsMax.z);
		}

		minZ += offsetZ;
		maxZ += offsetZ;


		return targetPos.x >= minX && targetPos.x <= maxX &&
			   targetPos.z >= minZ && targetPos.z <= maxZ;
	}
}
