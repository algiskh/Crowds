using UnityEngine;

[CreateAssetMenu(fileName = "MobConfigHolder", menuName = "Scriptable Objects/MobConfigHolder")]
public class MobConfigHolder : ScriptableObject
{
    [SerializeField] private MobConfig[] _mobConfigs;

    public MobConfig GetConfigById(string id)
    {
		foreach (var mob in _mobConfigs)
		{
			if (mob.Id == id)
			{
				return mob;
			}
		}
		Debug.LogWarning($"{nameof(MobConfigHolder)}: Mob with ID {id} not found.");
		return null;
	}
}
