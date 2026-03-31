using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class FxWrapper
{
    public string Id;
    public SceneEffect Prefab;
	public bool HasDuration = false;
	[ShowIf(nameof(HasDuration), true)]
	public float Duration;
	public bool IsChild;
}

[CreateAssetMenu(fileName = "FxHolder", menuName = "Scriptable Objects/FxHolder")]
public class EffectsHolder : ScriptableObject
{
	[SerializeField] private FxWrapper[] _fxWrappers;

	private static EffectsHolder _instance;
	public static EffectsHolder Instance
	{
		get
		{
			if (_instance == null)
			{
				_instance = Resources.Load<EffectsHolder>("FxHolder");
			}
			return _instance;
		}
	}

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

	public IEnumerable<FxWrapper> GetAll()
	{
		return _fxWrappers;
	}
}