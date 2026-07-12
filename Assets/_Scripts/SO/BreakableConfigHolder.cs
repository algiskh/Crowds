using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registry of <see cref="BreakableConfig"/> assets, looked up by id (mirrors <see cref="MobConfigHolder"/>).
/// Each config carries its own prefab, so systems can spawn environment objects at runtime by id via
/// RequestSpawnBreakableComponent. Referenced from <see cref="MainHolder"/>.
/// </summary>
[CreateAssetMenu(fileName = "BreakableConfigHolder", menuName = "Scriptable Objects/BreakableConfigHolder")]
public class BreakableConfigHolder : ScriptableObject
{
	[SerializeField] private BreakableConfig[] _configs;

	public IReadOnlyList<BreakableConfig> All => _configs;

	public BreakableConfig GetConfigById(string id)
	{
		if (_configs != null)
		{
			for (int i = 0; i < _configs.Length; i++)
			{
				if (_configs[i] != null && _configs[i].Id == id)
					return _configs[i];
			}
		}
		Debug.LogWarning($"{nameof(BreakableConfigHolder)}: Breakable with ID {id} not found.");
		return null;
	}
}
