using UnityEngine;

/// <summary>
/// Clipped boxy hedge on a lawn — a ModifierZone with no collider: whoever pushes through it (player or mob) is
/// slowed (by default a SpeedModifier 0.55 = −45%) through ModifierZoneSystem, like an OilSlick. The zone is the
/// hedge's footprint: a box <see cref="_size"/> (x = length, z = thickness) centred on the transform, scaled with it,
/// height ignored. The visual is a baked mesh built by SuburbBuilder (Hedge.prefab); mobs path straight through it
/// (NavMeshModifier.ignoreFromBuild). See Docs/SuburbFeature.md.
/// </summary>
public sealed class Hedge : ModifierZone
{
	[SerializeField, Tooltip("Footprint in metres (before transform scale): x = length, y = thickness.")]
	private Vector2 _size = new(6f, 1.2f);

	public Vector2 Size => _size;

	private void Reset() => ApplyDefaults(_size);

	/// <summary>Default slow + footprint (editor builders call it on a fresh component).</summary>
	public void ApplyDefaults(Vector2 size)
	{
		_size = size;
		SetDefaults(0.25f, new SpeedModifier
		{
			Id = ModifierConstants.SpeedHedge,
			Source = BuffSource.Environment,
			Value = 0.55f,
		});
	}

	public override bool Contains(Vector3 worldPosition)
	{
		Vector3 local = transform.InverseTransformPoint(worldPosition);
		return Mathf.Abs(local.x) <= _size.x * 0.5f && Mathf.Abs(local.z) <= _size.y * 0.5f;
	}

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0.3f, 0.85f, 0.3f, 0.8f);
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.DrawWireCube(new Vector3(0f, 0.05f, 0f), new Vector3(_size.x, 0.1f, _size.y));
	}
#endif
}
