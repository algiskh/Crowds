using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using System.Linq;
#endif

// Level content root — placed on the ROOT of the level prefab.
// The universal gameplay scene instantiates the prefab and reads the config and sectors from here.
// SpawnPoints, MapLoot and objects tagged "AdditionalSpawn" are discovered
// by EntryPoint via Find*/tag, so it's enough to just place them inside the prefab.
// See Docs/SectorFeature.md and the "level-prefabs" plan.
public class LevelRoot : MonoBehaviour
{
	[SerializeField, Required, BoxGroup("Level")]
	private LevelConfig _levelConfig;

	[SerializeField, BoxGroup("Level"),
	 Tooltip("Floor sectors in order along the Z axis. For Sliding mode the order matters. " +
	         "The button below collects them automatically from child objects.")]
	private FloorSector[] _sectors;

	[SerializeField, BoxGroup("Level"),
	 Tooltip("Optional: where the player starts (position + facing). The camera keeps its offset. Empty = scene position.")]
	private Transform _playerStart;

	public LevelConfig LevelConfig => _levelConfig;
	public Transform PlayerStart => _playerStart;
	public FloorSector[] Sectors => _sectors;

#if UNITY_EDITOR
	[Button("Collect sectors (sorted by Z)"), BoxGroup("Level")]
	private void CollectSectors()
	{
		_sectors = GetComponentsInChildren<FloorSector>(true)
			.OrderBy(s => s.transform.position.z)
			.ToArray();
		UnityEditor.EditorUtility.SetDirty(this);
	}
#endif
}
