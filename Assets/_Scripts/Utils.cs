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

	/// <summary>Product of MeleeStrengthModifier.AttackSpeed (Rage bonus); 1 when there is none.</summary>
	public static float GetMeleeAttackSpeed(this ModifierOwnerComponent modifiers)
	{
		float speed = 1f;
		if (modifiers.Modifiers == null)
			return speed;
		for (int i = 0; i < modifiers.Modifiers.Count; i++)
			if (modifiers.Modifiers[i] is MeleeStrengthModifier strength && strength.AttackSpeed > 0f)
				speed *= strength.AttackSpeed;
		return speed;
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

	// Transitional: delegate to the ECS.EffectPool service behind the bridge singleton (Docs/tasks/T-001.md).
	// Migrated systems inject EffectPool and call Release/Spawn directly.
	public static void Pool(this EffectPoolComponent pool, SceneEffect effect) => pool.Value.Release(effect);

	/// <summary>
	/// Pops an effect from the pool by config (or instantiates a new one) — for systems that need direct
	/// control over the effect's lifetime (e.g. the grenade's escorting trail).
	/// </summary>
	public static SceneEffect SpawnFromPool(this EffectPoolComponent pool, FxWrapper config) => pool.Value.Spawn(config);

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
