using System;
using UnityEngine;

[Serializable]
public class FxWrapper
{
    public string Id;
    public SceneEffect Prefab;
    public float Duration;
}

[CreateAssetMenu(fileName = "FxHolder", menuName = "Scriptable Objects/FxHolder")]
public class EffectsHolder : ScriptableObject
{
    [SerializeField] private FxWrapper[] _fxWrappers;

    public FxWrapper GetEffect(string id)
	{
		foreach (var fx in _fxWrappers)
		{
			if (fx.Id == id)
			{
				return fx;
			}
		}
		Debug.LogWarning($"Fx with ID {id} not found.");
		return null;
	}
}
