using UnityEngine;

/// <summary>
/// A named axis-aligned XZ area of the level (height ignored). Used by PlayerInZoneCondition — e.g. "the player
/// entered platform 2" or "reached the exit". Rotation is ignored. See Docs/KeyLevelFeature.md.
/// </summary>
public sealed class LevelZone : MonoBehaviour
{
	[SerializeField] private string _id = "zone";
	[SerializeField, Tooltip("XZ size in metres (Y unused).")] private Vector3 _size = new(6f, 2f, 6f);

	public string Id => _id;

	public bool Contains(Vector3 position)
	{
		Vector3 c = transform.position;
		return Mathf.Abs(position.x - c.x) <= _size.x * 0.5f && Mathf.Abs(position.z - c.z) <= _size.z * 0.5f;
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.25f);
		Gizmos.DrawCube(transform.position + Vector3.up * 0.05f, new Vector3(_size.x, 0.1f, _size.z));
		Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.9f);
		Gizmos.DrawWireCube(transform.position + Vector3.up * 0.05f, new Vector3(_size.x, 0.1f, _size.z));
	}
#endif
}
