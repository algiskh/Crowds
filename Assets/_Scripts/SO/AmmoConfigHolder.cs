using UnityEngine;

[CreateAssetMenu(fileName = "AmmoConfigHolder", menuName = "Scriptable Objects/AmmoConfigHolder")]
public class AmmoConfigHolder : ScriptableObject
{
	[SerializeField] private AmmoConfig[] _configs;

	// First configured ammo type — fallback caliber for unassigned ammo loot when there's no weapon.
	public AmmoConfig First
	{
		get
		{
			if (_configs == null)
				return null;
			foreach (var config in _configs)
				if (config != null)
					return config;
			return null;
		}
	}

	// Returns the config for a caliber, or null if none is set up (the caller applies a fallback).
	// No LogError: bullet spawning calls this every shot, and None is a valid "no caliber".
	public AmmoConfig GetConfig(Caliber caliber)
	{
		if (caliber == Caliber.None || _configs == null)
			return null;

		foreach (var config in _configs)
		{
			if (config != null && config.Caliber == caliber)
				return config;
		}
		return null;
	}
}
