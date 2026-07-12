using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Scene-placed level scripting object: an empty GameObject carrying a list of entries. Each entry
/// arms when its target <see cref="DifficultyLevel"/> stage starts and, once armed, fires its
/// spawn actions either immediately or when its optional smart conditions are all fulfilled.
/// Discovered by EntryPoint (FindObjectsByType) and driven by <c>LevelEventSystem</c>.
/// See Docs/LevelEventsFeature.md.
/// </summary>
public class LevelEventTrigger : SerializedMonoBehaviour
{
	[SerializeField, ListDrawerSettings(ShowFoldout = true)]
	private List<LevelEventEntry> _entries = new();

	public IReadOnlyList<LevelEventEntry> Entries => _entries;
}

/// <summary>
/// One scripted entry: when a difficulty stage starts (optionally gated by smart conditions),
/// spawn the configured breakables.
/// </summary>
[Serializable]
public class LevelEventEntry
{
	[Tooltip("Fires when this difficulty stage starts.")]
	public DifficultyLevel OnStageStart;

	[Tooltip("Optional gate: once the stage starts, actions run only when ALL of these are fulfilled. " +
	         "Empty = fire immediately on stage start.")]
	public SmartConditionWrapper[] Conditions;

	[Tooltip("Fire only once per run. If false, re-arms every time this stage starts (levels loop).")]
	public bool Once = true;

	[Tooltip("Breakables spawned when this entry fires.")]
	public SpawnBreakableAction[] Spawns;
}

/// <summary>
/// Spawn one breakable by config id at a point (or the trigger's own transform when Point is empty).
/// </summary>
[Serializable]
public class SpawnBreakableAction
{
	[Tooltip("Breakable config id (must exist in MainHolder.BreakableConfigHolder).")]
	[ValueDropdown(nameof(GetBreakableIds))]
	public string ConfigId;

	[Tooltip("Where to spawn. Empty = the trigger's own transform.")]
	public Transform Point;

	[Tooltip("Y rotation in degrees.")]
	public float Rotation;

	// Editor-only dropdown source: every BreakableConfig id in the project.
#if UNITY_EDITOR
	private static IEnumerable<string> GetBreakableIds()
	{
		var ids = new List<string>();
		foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:BreakableConfig"))
		{
			var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
			var config = UnityEditor.AssetDatabase.LoadAssetAtPath<BreakableConfig>(path);
			if (config != null && !string.IsNullOrEmpty(config.Id) && !ids.Contains(config.Id))
				ids.Add(config.Id);
		}
		ids.Sort();
		return ids;
	}
#else
	private static IEnumerable<string> GetBreakableIds() => System.Array.Empty<string>();
#endif
}
