using System;
using UnityEngine;

[Serializable]
public class AudioWrapper
{
	public string Id;
	public AudioClip Clip;
}

[CreateAssetMenu(fileName = "SoundHolder", menuName = "Scriptable Objects/SoundHolder")]
public class SoundHolder : ScriptableObject
{
	[SerializeField] private AudioWrapper[] _audioWrappers;

	public AudioClip GetClip(string id)
	{
		foreach (var wrapper in _audioWrappers)
		{
			if (wrapper.Id == id)
			{
				return wrapper.Clip;
			}
		}
		Debug.LogWarning($"Audio clip with ID '{id}' not found.");
		return null;
	}
}
