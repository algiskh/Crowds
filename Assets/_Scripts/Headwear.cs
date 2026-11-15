using UnityEngine;

/// <summary>
/// A hat/helmet worn by a mob (MobConfig.Headwear). Prefabs are built by Tools ▸ Crowds ▸ Build Helmet Models
/// (HelmetModelBuilder) from primitives and procedural shells — no textures.
///
/// Convention: root = the Head bone pivot (Mixamo "mixamorig:Head", the skull base), +Y toward the head top
/// ("mixamorig:HeadTop_End"), +Z = face forward, units = metres for a head <see cref="ReferenceHeadHeight"/> tall.
/// At spawn <see cref="AttachTo"/> measures the real Head→HeadTop distance and scales the helmet to it, so the same
/// prefab fits any Mixamo rig / MobConfig.Scale. Parented to the Head bone → follows run/attack/death animations.
/// Not for VAT crowd mobs (their bones don't move) — MobSpawnSystem skips headwear when MobConfig.CrowdLibrary is set.
/// </summary>
public sealed class Headwear : MonoBehaviour
{
	[Tooltip("Head bone → head top distance (m) the child parts are modelled for.")]
	[SerializeField, Min(0.01f)] private float _referenceHeadHeight = 0.2f;

	[Tooltip("Fine-tune in head space (reference metres): +Y up, +Z forward. Kept when the builder re-runs.")]
	[SerializeField] private Vector3 _offset = Vector3.zero;

	[Tooltip("Extra uniform size multiplier on top of the measured fit. Kept when the builder re-runs.")]
	[SerializeField, Min(0.1f)] private float _extraScale = 1f;

	public float ReferenceHeadHeight => _referenceHeadHeight;

	private const string HeadBone = "Head";
	private const string HeadTopBone = "HeadTop_End";

	/// <summary>
	/// Instantiates <paramref name="prefab"/> on <paramref name="mob"/>'s head bone, fitted to the head size.
	/// Call on a freshly instantiated mob (bind pose: head upright, facing the root's forward).
	/// Falls back to the mob root (at ~1.6 m × scale) when the rig has no Head bone.
	/// </summary>
	public static Headwear AttachTo(Transform mob, Headwear prefab)
	{
		if (mob == null || prefab == null)
			return null;

		Transform head = FindBone(mob, HeadBone);
		Transform top = head != null ? FindBone(head, HeadTopBone) : null;

		Vector3 basePos;
		Vector3 up;
		float height;
		if (head != null && top != null && (top.position - head.position).sqrMagnitude > 1e-6f)
		{
			basePos = head.position;
			Vector3 axis = top.position - head.position;
			height = axis.magnitude;
			up = axis / height;
		}
		else
		{
			if (head == null)
				Debug.LogWarning($"[Headwear] No '{HeadBone}' bone under '{mob.name}' — helmet put on the root.", mob);
			float s = mob.lossyScale.y;
			basePos = head != null ? head.position : mob.position + mob.up * (1.6f * s);
			up = mob.up;
			height = prefab._referenceHeadHeight * s;
		}

		Vector3 forward = Vector3.ProjectOnPlane(mob.forward, up);
		if (forward.sqrMagnitude < 1e-6f)
			forward = Vector3.ProjectOnPlane(mob.up, up);
		var rotation = Quaternion.LookRotation(forward.normalized, up);

		float scale = height / prefab._referenceHeadHeight * prefab._extraScale;
		var instance = Instantiate(prefab);
		instance.name = prefab.name;
		var t = instance.transform;
		t.localScale = Vector3.one * scale;
		t.SetPositionAndRotation(basePos + rotation * (prefab._offset * scale), rotation);
		t.SetParent(head != null ? head : mob, worldPositionStays: true);
		return instance;
	}

	// Exact name or Mixamo-prefixed ("mixamorig:Head", "mixamorig1:Head"). Depth-first.
	private static Transform FindBone(Transform root, string name)
	{
		for (int i = 0; i < root.childCount; i++)
		{
			var child = root.GetChild(i);
			string n = child.name;
			if (n == name || (n.EndsWith(name) && n.Length > name.Length && n[n.Length - name.Length - 1] == ':'))
				return child;
			var found = FindBone(child, name);
			if (found != null)
				return found;
		}
		return null;
	}
}
