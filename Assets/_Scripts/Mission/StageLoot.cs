using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Loot that appears on the level when a difficulty stage starts (MissionSystem): each entry is spawned once,
/// spread around this transform, through the regular loot pipeline (RequestLootSpawn, Source = MapLoot → no
/// despawn timer). Use it for "supplies after the wave" or a weapon that "appears" later. Entry Chance is ignored.
/// See Docs/KeyLevelFeature.md.
/// </summary>
public sealed class StageLoot : MonoBehaviour
{
	[SerializeField] private DifficultyLevel _onStage = DifficultyLevel.level2;
	[SerializeField, MinValue(0f), Tooltip("Entries are placed on a circle of this radius around the point.")]
	private float _spread = 1.5f;
	[SerializeField] private PossibleLoot[] _loot = System.Array.Empty<PossibleLoot>();

	public DifficultyLevel OnStage => _onStage;
	public float Spread => _spread;
	public PossibleLoot[] Loot => _loot;

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.9f);
		Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.3f, _spread));
	}
#endif
}
