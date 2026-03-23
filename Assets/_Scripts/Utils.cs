using UnityEngine;

public static class Utils
{
	public static float GetModifier<T>(this ModifierOwnerComponent moveComponent)
	{
		float modifier = 1f;
		foreach (var mod in moveComponent.Modifiers)
		{
			if (mod is T)
			{
				modifier *= mod.Value;
			}
		}
		return modifier;
	}
}
