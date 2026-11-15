using UnityEngine;

/// <summary>
/// Black oil puddle on the floor — a ModifierZone: whoever stands on it (player or mob) gets its modifiers
/// (by default a SpeedModifier 0.75 = −25%) through ModifierZoneSystem.
/// The shape is an irregular blob — a radius wobbling around <see cref="_radius"/> by a few seeded harmonics; the same
/// function draws the mesh and answers <see cref="Contains"/>, so the slow zone matches what you see.
/// The mesh is generated on enable (also in edit mode), so every instance can have its own <see cref="_seed"/>.
/// See Docs/KeyLevelFeature.md.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class OilSlick : ModifierZone
{
	private const int Harmonics = 3;
	private const int Segments = 48;

	[SerializeField, Min(0.2f), Tooltip("Mean radius in metres (before transform scale).")]
	private float _radius = 1.8f;
	[SerializeField, Range(0f, 0.45f), Tooltip("How irregular the outline is (fraction of the radius).")]
	private float _irregularity = 0.3f;
	[SerializeField, Tooltip("Shape seed: same seed = same blob.")]
	private int _seed = 1;

	private readonly float[] _amp = new float[Harmonics];
	private readonly float[] _phase = new float[Harmonics];
	private Mesh _mesh;

	private void Reset() =>
		SetDefaults(0.25f, new SpeedModifier
		{
			Id = ModifierConstants.SpeedOilSlick,
			Source = BuffSource.Environment,
			Value = 0.75f,
		});

	protected override void OnEnable()
	{
		base.OnEnable();
		RebuildShape();
	}

	private void OnDestroy()
	{
		if (_mesh == null)
			return;
		if (Application.isPlaying) Destroy(_mesh);
		else DestroyImmediate(_mesh);
	}

#if UNITY_EDITOR
	private void OnValidate()
	{
		// Mesh changes aren't allowed inside OnValidate — rebuild on the next editor tick.
		UnityEditor.EditorApplication.delayCall += () =>
		{
			if (this != null && isActiveAndEnabled)
				RebuildShape();
		};
	}
#endif

	/// <summary>Outline radius (local units) in the local direction <paramref name="angle"/> (radians).</summary>
	public float RadiusAt(float angle)
	{
		float r = 1f;
		for (int i = 0; i < Harmonics; i++)
			r += _amp[i] * Mathf.Sin((i + 2) * angle + _phase[i]);
		return _radius * r;
	}

	/// <summary>True when <paramref name="worldPosition"/> is over the slick (height ignored).</summary>
	public override bool Contains(Vector3 worldPosition)
	{
		Vector3 local = transform.InverseTransformPoint(worldPosition);
		float sqr = local.x * local.x + local.z * local.z;
		float max = _radius * (1f + _irregularity);
		if (sqr > max * max)
			return false;
		float r = RadiusAt(Mathf.Atan2(local.z, local.x));
		return sqr <= r * r;
	}

	private void RebuildShape()
	{
		var random = new System.Random(_seed);
		float total = 0f;
		for (int i = 0; i < Harmonics; i++)
		{
			_amp[i] = (float)random.NextDouble() / (i + 1);
			_phase[i] = (float)random.NextDouble() * Mathf.PI * 2f;
			total += _amp[i];
		}
		// Normalize: the wobble never exceeds ±_irregularity.
		for (int i = 0; i < Harmonics; i++)
			_amp[i] = total > 0f ? _amp[i] / total * _irregularity : 0f;

		BuildMesh();
	}

	// Triangle fan: centre + outline; UVs map the blob into 0..1 (for a gloss/noise texture if one is added).
	private void BuildMesh()
	{
		if (_mesh == null)
		{
			_mesh = new Mesh { name = "OilSlick", hideFlags = HideFlags.DontSave };
		}
		var vertices = new Vector3[Segments + 1];
		var uvs = new Vector2[Segments + 1];
		var normals = new Vector3[Segments + 1];
		var triangles = new int[Segments * 3];
		float extent = _radius * (1f + _irregularity);
		vertices[0] = Vector3.zero;
		uvs[0] = new Vector2(0.5f, 0.5f);
		for (int i = 0; i < Segments; i++)
		{
			float a = i * Mathf.PI * 2f / Segments;
			float r = RadiusAt(a);
			var v = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
			vertices[i + 1] = v;
			uvs[i + 1] = new Vector2(0.5f + v.x / (2f * extent), 0.5f + v.z / (2f * extent));
		}
		for (int i = 0; i < normals.Length; i++)
			normals[i] = Vector3.up;
		for (int i = 0; i < Segments; i++)
		{
			// Clockwise seen from above (+Y) = front face up.
			triangles[i * 3] = 0;
			triangles[i * 3 + 1] = i + 2 > Segments ? 1 : i + 2;
			triangles[i * 3 + 2] = i + 1;
		}
		_mesh.Clear();
		_mesh.vertices = vertices;
		_mesh.uv = uvs;
		_mesh.normals = normals;
		_mesh.triangles = triangles;
		_mesh.RecalculateBounds();
		GetComponent<MeshFilter>().sharedMesh = _mesh;
	}
}
