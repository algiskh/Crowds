using UnityEngine;

/// <summary>
/// Shallow water (backyard swimming pool) — a box ModifierZone: whoever wades through it, player or mob, is slowed
/// (by default a SpeedModifier 0.5 = −50%) through ModifierZoneSystem. Footprint <see cref="_size"/> (x, z) centred on
/// the transform, height ignored. The visual (coping, water, deck, ladder) is a baked mesh built by
/// SuburbLevelsBuilder (Prop_Pool.prefab). See Docs/SuburbLevelsFeature.md.
/// </summary>
public sealed class ShallowWater : ModifierZone
{
	[SerializeField, Tooltip("Footprint in metres (before transform scale): x = length, y = width.")]
	private Vector2 _size = new(9f, 4.5f);

	[SerializeField, Min(0f), Tooltip("How deep a wader's visual sinks in the middle of the pool, metres (WadeSystem). " +
	                                  "~1/3 of the player's height.")]
	private float _sinkDepth = 0.6f;
	[SerializeField, Min(0.01f), Tooltip("Distance from the edge over which the sink ramps up (the pool steps), metres.")]
	private float _sinkRamp = 0.9f;

	public Vector2 Size => _size;
	public float SinkDepth => _sinkDepth;

	/// <summary>Visual sink at <paramref name="worldPosition"/>: 0 outside, ramping to SinkDepth away from the edges.</summary>
	public float SinkAt(Vector3 worldPosition)
	{
		Vector3 local = transform.InverseTransformPoint(worldPosition);
		float inside = Mathf.Min(_size.x * 0.5f - Mathf.Abs(local.x), _size.y * 0.5f - Mathf.Abs(local.z));
		return inside <= 0f ? 0f : _sinkDepth * Mathf.Clamp01(inside / _sinkRamp);
	}

	private void Reset() => ApplyDefaults(_size);

	/// <summary>Default slow + footprint (editor builders call it on a fresh component).</summary>
	public void ApplyDefaults(Vector2 size)
	{
		_size = size;
		SetDefaults(0.3f, new SpeedModifier
		{
			Id = ModifierConstants.SpeedWater,
			Source = BuffSource.Environment,
			Value = 0.5f,
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
		Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.DrawWireCube(new Vector3(0f, 0.05f, 0f), new Vector3(_size.x, 0.1f, _size.y));
	}
#endif
}
