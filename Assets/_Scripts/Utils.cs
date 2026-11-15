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

	/// <summary>Lowest Value among modifiers of type T, or <paramref name="fallback"/> when there is none.</summary>
	public static float GetMinModifier<T>(this ModifierOwnerComponent modifiers, float fallback)
	{
		float result = fallback;
		bool found = false;
		if (modifiers.Modifiers != null)
		{
			for (int i = 0; i < modifiers.Modifiers.Count; i++)
			{
				var mod = modifiers.Modifiers[i];
				if (mod is T && (!found || mod.Value < result))
				{
					result = mod.Value;
					found = true;
				}
			}
		}
		return result;
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

	/// <summary>
	/// Pops an effect from the pool by config (or instantiates a new one). A mirror of the private
	/// SpawnEffect in EffectsSystem — for systems that need direct control over the effect's
	/// lifetime (e.g. the grenade's escorting trail).
	/// </summary>
	public static SceneEffect SpawnFromPool(this EffectPoolComponent pool, FxWrapper config)
	{
		if (config == null || config.Prefab == null) return null;

		SceneEffect effect;
		if (pool.Pools != null && pool.Pools.TryGetValue(config.Id, out var stack) && stack.Count > 0)
		{
			effect = stack.Pop();
		}
		else
		{
			effect = UnityEngine.Object.Instantiate(config.Prefab, pool.Parent);
			effect.Initialize(config.Id);
		}
		return effect;
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
