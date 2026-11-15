using UnityEngine;

/// <summary>
/// Hockey ice — a ModifierZone shaped as a rounded rectangle (<see cref="_size"/> in local X/Z,
/// <see cref="_cornerRadius"/>), centred on the transform. By default it gives a TractionModifier (grip 3 m/s²):
/// players and mobs on the ice move inertially and struggle to turn/stop (Inertia, PlayerMovementSystem,
/// MoveSystem). The ice surface itself is regular level geometry (HockeyLevelBuilder). See Docs/HockeyLevelFeature.md.
/// </summary>
public sealed class IceRink : ModifierZone
{
	[SerializeField, Tooltip("Rink length (local X) × width (local Z), metres.")]
	private Vector2 _size = new(60f, 30f);
	[SerializeField, Min(0f)] private float _cornerRadius = 8.5f;

	public Vector2 Size => _size;
	public float CornerRadius => _cornerRadius;

	private void Reset() =>
		SetDefaults(0.1f, new TractionModifier
		{
			Id = ModifierConstants.TractionIce,
			Source = BuffSource.Environment,
			Value = 3f,
		});

	public override bool Contains(Vector3 worldPosition)
	{
		Vector3 local = transform.InverseTransformPoint(worldPosition);
		float hx = _size.x * 0.5f, hz = _size.y * 0.5f;
		float r = Mathf.Min(_cornerRadius, Mathf.Min(hx, hz));
		float dx = Mathf.Abs(local.x) - (hx - r);
		float dz = Mathf.Abs(local.z) - (hz - r);
		if (dx > r || dz > r)
			return false;
		if (dx <= 0f || dz <= 0f)
			return true;
		return dx * dx + dz * dz <= r * r;
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.8f);
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.DrawWireCube(Vector3.up * 0.05f, new Vector3(_size.x, 0.1f, _size.y));
	}
#endif
}
