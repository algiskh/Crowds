using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Cheerleader stage in a corner of the hockey arena (built by HockeyLevelBuilder) and the data of the hidden
/// "don't forget the girls" mini-quest, driven by ECS.CheerleaderSystem:
///  • <see cref="Cheerleaders"/> dance on <see cref="Slots"/> (forward = toward the ice / camera);
///  • when a transform stage (breather) starts and none of them is on screen at that moment, they vanish and
///    <see cref="ZombieMobId"/> mobs spawn at <see cref="FloorPoints"/> + the <see cref="ForgottenHint"/> log line;
///  • on player death they jump down to <see cref="FloorPoints"/> (the narrow passage in front of the stage) and run
///    to <see cref="EscapePoint"/> (the opposite corner) — the zombies retarget to them.
/// The deck is Not Walkable for the bake. See Docs/CheerleaderFeature.md.
/// </summary>
public sealed class CheerleaderStand : MonoBehaviour
{
	[BoxGroup("Cheerleaders"), Tooltip("NPC instances on the deck, same order as the slots.")]
	[SerializeField] private Cheerleader[] _cheerleaders;
	[BoxGroup("Anchors"), Tooltip("Dancer spots on the deck, forward faces the ice.")]
	[SerializeField] private Transform[] _slots;
	[BoxGroup("Anchors"), Tooltip("Points on the floor in front of the stage (navmesh): escape start / zombie spawns.")]
	[SerializeField] private Transform[] _floorPoints;
	[BoxGroup("Anchors"), Tooltip("Where they run on player death — the far side of the level.")]
	[SerializeField] private Transform _escapePoint;

	[BoxGroup("Quest"), Tooltip("Stages during which off-screen cheerleaders turn into zombies (the breathers).")]
	[SerializeField] private DifficultyLevel[] _transformStages = { DifficultyLevel.level2, DifficultyLevel.level4 };
	[BoxGroup("Quest"), Tooltip("MobConfig id of the zombie cheerleaders (Fast clone until their own model exists).")]
	[SerializeField] private string _zombieMobId = "CheerZombie";
	[BoxGroup("Quest")]
	[SerializeField] private string _forgottenHint = "вы забыли про девочек";

	public Cheerleader[] Cheerleaders => _cheerleaders;
	public Transform[] Slots => _slots;
	public Transform[] FloorPoints => _floorPoints;
	public Transform EscapePoint => _escapePoint;
	public string ZombieMobId => _zombieMobId;
	public string ForgottenHint => _forgottenHint;

	public bool IsTransformStage(DifficultyLevel level)
	{
		if (_transformStages == null)
			return false;
		for (int i = 0; i < _transformStages.Length; i++)
			if (_transformStages[i] == level)
				return true;
		return false;
	}

	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(1f, 0.4f, 0.8f);
		if (_slots != null)
			foreach (var slot in _slots)
				if (slot != null)
				{
					Gizmos.DrawWireSphere(slot.position + Vector3.up * 0.9f, 0.3f);
					Gizmos.DrawLine(slot.position + Vector3.up * 0.9f, slot.position + Vector3.up * 0.9f + slot.forward * 0.6f);
				}
		if (_floorPoints != null)
			foreach (var point in _floorPoints)
				if (point != null)
					Gizmos.DrawWireSphere(point.position, 0.25f);
		if (_escapePoint != null)
		{
			Gizmos.color = new Color(1f, 0.4f, 0.8f, 0.5f);
			Gizmos.DrawLine(transform.position, _escapePoint.position);
			Gizmos.DrawWireSphere(_escapePoint.position, 1f);
		}
	}
}
