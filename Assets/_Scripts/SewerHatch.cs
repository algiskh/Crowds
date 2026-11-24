using UnityEngine;

/// <summary>
/// Stinking sewer hatch — a round ModifierZone (<see cref="_radius"/>, centred on the transform, scaled with it).
/// Whoever stands on the lid gets its modifiers through ModifierZoneSystem: by default a light poison
/// DamageModifier (1 HP every 0.5 s) for the player only — the toxic zombies crawl out of these hatches, so mobs
/// are immune (ModifierZone.AffectsMobs = false). The visual (lid, grate, green stench) is built by
/// KeyLevelBuilder.BuildHatchPrefab. Used on Level_02_Key and as the toxic-zombie spawns of Infinite Scroll
/// (SewerHatchBuilder). See Docs/SewerHatchFeature.md.
/// </summary>
public sealed class SewerHatch : ModifierZone
{
	[SerializeField, Min(0.1f), Tooltip("Zone radius in metres (before transform scale).")]
	private float _radius = 0.75f;

	public float Radius => _radius;

	private void Reset()
	{
		SetDefaults(0.3f, CreateDefaultStench());
		SetMobs(false);
		SetEntryEffect(true);
	}

	/// <summary>Default hatch damage: light poison, 1 HP per 0.5 s, with the toxic VFX on the victim.</summary>
	public static DamageModifier CreateDefaultStench() => new()
	{
		Id = ModifierConstants.DamageSewerStench,
		Source = BuffSource.Environment,
		Type = DamageType.Poison,
		Value = 1f,
		Interval = 0.5f,
		HasEffect = true,
		EffectId = "toxicDamage",
	};

	public override bool Contains(Vector3 worldPosition)
	{
		Vector3 local = transform.InverseTransformPoint(worldPosition);
		return local.x * local.x + local.z * local.z <= _radius * _radius;
	}

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(0.45f, 0.9f, 0.2f, 0.8f);
		Gizmos.matrix = transform.localToWorldMatrix;
		const int segments = 32;
		for (int i = 0; i < segments; i++)
		{
			float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
			Gizmos.DrawLine(new Vector3(Mathf.Cos(a0) * _radius, 0.05f, Mathf.Sin(a0) * _radius),
				new Vector3(Mathf.Cos(a1) * _radius, 0.05f, Mathf.Sin(a1) * _radius));
		}
	}
#endif
}
