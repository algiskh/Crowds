using UnityEngine;

[CreateAssetMenu(fileName = "GunConfigHolder", menuName = "Scriptable Objects/GunConfigHolder")]
public class GunConfigHolder : ScriptableObject
{
    [SerializeField] private GunConfig[] _configs;

    public GunConfig GetConfig(string id)
	{
		foreach (var config in _configs)
		{
			if (config.Id == id)
			{
				return config;
			}
		}
		Debug.LogError($"GunConfig with ID {id} not found.");
		return null;
	}
}
