using Sirenix.OdinInspector;
using UnityEngine;

// One level for the menu: content prefab + display metadata.
// The gameplay config (LevelConfig) lives on the LevelRoot inside the prefab - a single source
// of truth, so the prefab is self-contained. Here only what the menu needs before instantiation.
[CreateAssetMenu(fileName = "LevelDefinition", menuName = "Scriptable Objects/LevelDefinition")]
public class LevelDefinition : ScriptableObject
{
	[Title("Content")]
	[SerializeField, Required,
	 ValidateInput(nameof(HasLevelRoot), "The prefab root must have a LevelRoot component"),
	 Tooltip("Level prefab: FloorSectors + placed loot/spawn points, with LevelRoot on the root.")]
	private GameObject _levelPrefab;

	[Title("Menu")]
	[SerializeField] private string _displayName;
	[SerializeField, PreviewField(60)] private Sprite _icon;

	public GameObject LevelPrefab => _levelPrefab;
	public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
	public Sprite Icon => _icon;

	private bool HasLevelRoot(GameObject prefab) =>
		prefab == null || prefab.GetComponent<LevelRoot>() != null;
}
