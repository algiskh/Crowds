using System.Collections.Generic;
using System;
using System.Linq;
using UnityEngine;

public static class Extensions
{
	public static T GetRandomElement<T>(this IEnumerable<T> source, bool throwException = true)
	{
		if (source == null || !source.Any())
		{
			if (throwException)
			{
				throw new InvalidOperationException("Cannot select a random element from an empty or null collection.");
			}

			return default;
		}

		var random = new System.Random();

		var index = random.Next(0, source.Count());
		return source.ElementAt(index);
	}

	public static float DistanceX(this Vector2 a, Vector2 b)
	{
		return Math.Abs(a.x - b.x);
	}

	public static float DistanceTo(this Vector3 a, Vector3 b)
	{
		return Vector3.Distance(a,b);
	}
}
