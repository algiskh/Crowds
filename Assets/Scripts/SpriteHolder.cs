using System;
using UnityEngine;


[Serializable]
public struct SpriteWrapper
{
    public string Id;
    public Sprite Sprite;
}

[CreateAssetMenu(fileName = "SpriteHolder", menuName = "Scriptable Objects/SpriteHolder")]
public class SpriteHolder : ScriptableObject
{
    [SerializeField] private SpriteWrapper[] _sprites;

	public Sprite GetSpriteById(string id)
	{
		foreach (var spriteWrapper in _sprites)
		{
			if (spriteWrapper.Id == id)
			{
				return spriteWrapper.Sprite;
			}
		}
		Debug.LogWarning($"Sprite with ID {id} not found.");
		return null;
	}
}
