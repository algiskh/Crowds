using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solid level geometry that stops bullets of both teams (walls, closed gate panels). No collider/physics layer
/// needed: BulletOverlapSystem tests each bullet's per-frame path against this box in the blocker's local space
/// (so rotated walls work). The box is <see cref="_size"/> around <see cref="_center"/> in local units, i.e. for a
/// scaled unit cube leave the defaults. Active while the GameObject is active (static registry, OnEnable/OnDisable),
/// so a gate panel that hides itself when open stops blocking. See Docs/KeyLevelFeature.md.
/// </summary>
public sealed class BulletBlocker : MonoBehaviour
{
	private static readonly List<BulletBlocker> ActiveList = new();

	/// <summary>All enabled blockers (read-only use).</summary>
	public static List<BulletBlocker> Active => ActiveList;

	[SerializeField, Tooltip("Box centre in local space.")] private Vector3 _center = Vector3.zero;
	[SerializeField, Tooltip("Box size in local space (unit cube = 1,1,1).")] private Vector3 _size = Vector3.one;
	[SerializeField, Tooltip("Effect played where a bullet hits (EffectsHolder id). Empty = none.")]
	private string _impactEffectId = string.Empty;

	public string ImpactEffectId => _impactEffectId;

	private void OnEnable()
	{
		if (!ActiveList.Contains(this))
			ActiveList.Add(this);
	}

	private void OnDisable() => ActiveList.Remove(this);

	/// <summary>
	/// Segment <paramref name="from"/>→<paramref name="to"/> (world) vs the box. Returns true and the world hit point
	/// of the first entry; a segment that starts inside the box hits at its start.
	/// </summary>
	public bool Intersects(Vector3 from, Vector3 to, out Vector3 hitPoint)
	{
		hitPoint = default;
		var t = transform;
		Vector3 a = t.InverseTransformPoint(from) - _center;
		Vector3 b = t.InverseTransformPoint(to) - _center;
		Vector3 half = _size * 0.5f;
		Vector3 d = b - a;

		float tMin = 0f, tMax = 1f;
		for (int axis = 0; axis < 3; axis++)
		{
			float origin = a[axis], dir = d[axis], h = half[axis];
			if (Mathf.Abs(dir) < 1e-7f)
			{
				if (origin < -h || origin > h)
					return false;
				continue;
			}
			float inv = 1f / dir;
			float t1 = (-h - origin) * inv, t2 = (h - origin) * inv;
			if (t1 > t2) (t1, t2) = (t2, t1);
			if (t1 > tMin) tMin = t1;
			if (t2 < tMax) tMax = t2;
			if (tMin > tMax)
				return false;
		}
		hitPoint = Vector3.Lerp(from, to, tMin);
		return true;
	}

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.8f);
		Gizmos.matrix = transform.localToWorldMatrix;
		Gizmos.DrawWireCube(_center, _size);
	}
#endif
}
