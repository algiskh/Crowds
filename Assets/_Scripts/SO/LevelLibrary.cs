using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

// Ordered list of all levels. The menu reads it to build the level selection.
[CreateAssetMenu(fileName = "LevelLibrary", menuName = "Scriptable Objects/LevelLibrary")]
public class LevelLibrary : ScriptableObject
{
	[SerializeField, ListDrawerSettings(ShowFoldout = true)]
	private List<LevelDefinition> _levels = new();

	public IReadOnlyList<LevelDefinition> Levels => _levels;
	public int Count => _levels.Count;

	public LevelDefinition Get(int index) =>
		index >= 0 && index < _levels.Count ? _levels[index] : null;

	public LevelDefinition First => _levels.Count > 0 ? _levels[0] : null;
}
