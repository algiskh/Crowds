using UnityEngine;

[CreateAssetMenu(fileName = "BonusConfigHolder", menuName = "Scriptable Objects/BonusConfigHolder")]
public class BonusConfigHolder : ScriptableObject
{
	[SerializeField] private BonusConfig[] _configs;

	/// <summary>First config - the default for loot without an explicit Id.</summary>
	public BonusConfig Default => _configs != null && _configs.Length > 0 ? _configs[0] : null;

	public BonusConfig GetConfig(string id)
	{
		if (_configs != null)
		{
			foreach (var config in _configs)
			{
				if (config != null && config.Id == id)
					return config;
			}
		}
		Debug.LogError($"BonusConfig with ID {id} not found. Falling back to default.");
		return Default;
	}
}
