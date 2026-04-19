using System;
using Unity.Collections;
using Unity.VisualScripting;
using UnityEngine;

public static class Utils
{
	public static float GetModifier<T>(this ModifierOwnerComponent modifiers)
	{
		float modifier = 1f;

		if (modifiers.Modifiers != null && modifiers.Modifiers.Count > 0)
		{
			foreach (var mod in modifiers.Modifiers)
			{
				if (mod is T)
				{
					modifier *= mod.Value;
				}
			}
		}
		return modifier;
	}

	public static bool HasModifierWithDamageType(this ModifierOwnerComponent modifiers, DamageType type)
	{
		if (modifiers.Modifiers != null && modifiers.Modifiers.Count > 0)
		{
			foreach (var mod in modifiers.Modifiers)
			{
				if (mod is DamageModifier damageMod && damageMod.Type == type)
				{
					return true;
				}
			}
		}
		return false;
	}

	public static void Pool(this EffectPoolComponent pool, SceneEffect effect)
	{
		effect.Hide();
		effect.SetParent(pool.Parent.transform);
		if (pool.Pools == null) return;
		if (!pool.Pools.TryGetValue(effect.Id, out var stack))
		{
			stack = new System.Collections.Generic.Stack<SceneEffect>();
			pool.Pools[effect.Id] = stack;
		}
		stack.Push(effect);
	}

	public static Vector3 GetForwardPosition(this Transform original, float range)
	{
		return original.position + original.forward * range;
	}

	public static bool ContainsFlags<T>(this T a, T b)
	where T : struct, Enum
	{
		ulong aValue = Convert.ToUInt64(a);
		ulong bValue = Convert.ToUInt64(b);

		if (bValue == 0)
			return false;

		return (aValue & bValue) == bValue;
	}

	public static bool ContainsFixed(this FixedList32Bytes<int> list, int value)
	{
		for (int i = 0; i < list.Length; i++)
		{
			if (list[i] == value)
				return true;
		}
		return false;
	}
}
