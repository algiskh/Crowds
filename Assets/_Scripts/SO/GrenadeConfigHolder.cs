using UnityEngine;

[CreateAssetMenu(fileName = "GrenadeConfigHolder", menuName = "Scriptable Objects/GrenadeConfigHolder")]
public class GrenadeConfigHolder : ScriptableObject
{
	[SerializeField] private GrenadeConfig[] _configs;

	/// <summary>Первый конфиг — дефолт для стартовых гранат без явного Id.</summary>
	public GrenadeConfig Default => _configs != null && _configs.Length > 0 ? _configs[0] : null;

	public GrenadeConfig GetConfig(string id)
	{
		if (_configs != null)
		{
			foreach (var config in _configs)
			{
				if (config != null && config.Id == id)
					return config;
			}
		}
		Debug.LogError($"GrenadeConfig with ID {id} not found. Falling back to default.");
		return Default;
	}
}
