using UnityEngine;

namespace Scene.Animation
{
	/// <summary>
	/// Standalone validation for the VAT pipeline (no ECS). Draws a grid of GPU-instanced mobs from a
	/// baked <see cref="CrowdAnimationLibrary"/> using Graphics.DrawMeshInstanced, each with its own time
	/// offset so the crowd doesn't march in lockstep. Use it to eyeball the bake + shader and to profile
	/// draw calls / frame time before wiring VAT into the spawn/pool systems (Stage B).
	///
	/// Put it on an empty GameObject, assign the library, press Play.
	/// </summary>
	public class CrowdRenderTester : MonoBehaviour
	{
		private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard limit per call

		[SerializeField] private CrowdAnimationLibrary _library;
		[SerializeField] private AnimationType _clip = AnimationType.Run;
		[SerializeField] private int _count = 500;
		[SerializeField] private float _spacing = 1.5f;
		[SerializeField] private bool _castShadows = true;

		private Matrix4x4[] _matrices;
		private float[] _frame;
		private Vector4[] _colors;
		private float[] _timeOffsets;
		private MaterialPropertyBlock _mpb;
		private int _frameId;
		private int _colorId;
		private int _vatWId, _vatHId, _vatVcId;

		// Reused per-batch buffers (DrawMeshInstanced caps at 1023 instances/call).
		private readonly Matrix4x4[] _batchMatrices = new Matrix4x4[BatchSize];
		private readonly float[] _batchFrame = new float[BatchSize];
		private readonly Vector4[] _batchColors = new Vector4[BatchSize];

		[SerializeField] private bool _verbose = true;
		private bool _loggedFirstDraw;

		private void Start()
		{
			_frameId = Shader.PropertyToID("_Frame");
			_colorId = Shader.PropertyToID("_InstColor");
			_vatWId = Shader.PropertyToID("_VatWidth");
			_vatHId = Shader.PropertyToID("_VatHeight");
			_vatVcId = Shader.PropertyToID("_VatVertexCount");
			_mpb = new MaterialPropertyBlock();
			Build();
			LogDiagnostics();
		}

		private void LogDiagnostics()
		{
			if (!_verbose) return;

			if (_library == null) { Debug.LogError("[CrowdTester] Library is NOT assigned.", this); return; }

			var mesh = _library.RenderMesh;
			var mat = _library.Material;
			Debug.Log($"[CrowdTester] instancingSupported={SystemInfo.supportsInstancing}", this);
			Debug.Log($"[CrowdTester] mesh={(mesh ? mesh.name : "NULL")} verts={(mesh ? mesh.vertexCount : 0)} " +
			          $"subMeshes={(mesh ? mesh.subMeshCount : 0)} bounds={(mesh ? mesh.bounds.ToString() : "-")}", this);
			Debug.Log($"[CrowdTester] material={(mat ? mat.name : "NULL")} shader={(mat && mat.shader ? mat.shader.name : "NULL")} " +
			          $"instancing={(mat && mat.enableInstancing)}", this);
			Debug.Log($"[CrowdTester] posMap={(_library.PositionMap ? $"{_library.PositionMap.width}x{_library.PositionMap.height} {_library.PositionMap.format}" : "NULL")} " +
			          $"vatW={_library.TextureWidth} vatH={_library.TextureHeight} vertexCount={_library.VertexCount} frames={_library.FrameCount}", this);
			if (mat != null)
				Debug.Log($"[CrowdTester] material uniforms: _VatWidth={mat.GetFloat("_VatWidth")} _VatHeight={mat.GetFloat("_VatHeight")} " +
				          $"_VatVertexCount={mat.GetFloat("_VatVertexCount")} (0 => NaN positions => nothing draws)", this);
			Debug.Log($"[CrowdTester] clips={_library.Clips.Count}", this);
			foreach (var c in _library.Clips)
				Debug.Log($"[CrowdTester]   clip {c.Type} start={c.StartFrame} count={c.FrameCount} loop={c.Loop}", this);

			if (!_library.TryGetClip(_clip, out var clip))
				Debug.LogError($"[CrowdTester] No clip for {_clip}; nothing will animate/draw.", this);
			else
				Debug.Log($"[CrowdTester] using clip {_clip} -> start={clip.StartFrame} count={clip.FrameCount}. " +
				          $"count={_count} spacing={_spacing} origin={transform.position}", this);
		}

		private void OnValidate()
		{
			if (Application.isPlaying && _library != null)
				Build();
		}

		private void Build()
		{
			_count = Mathf.Max(1, _count);
			_matrices = new Matrix4x4[_count];
			_frame = new float[_count];
			_colors = new Vector4[_count];
			_timeOffsets = new float[_count];

			int side = Mathf.CeilToInt(Mathf.Sqrt(_count));
			for (int i = 0; i < _count; i++)
			{
				int x = i % side;
				int z = i / side;
				var pos = new Vector3((x - side * 0.5f) * _spacing, 0f, (z - side * 0.5f) * _spacing);
				var rot = Quaternion.Euler(0f, (x * 37 + z * 53) % 360, 0f);
				_matrices[i] = Matrix4x4.TRS(transform.position + pos, rot, Vector3.one);
				_colors[i] = Color.white;
				// Deterministic spread of phases without Random (keeps it stable in editor).
				_timeOffsets[i] = (i * 0.6180339887f) % 1f;
			}
		}

		private void Update()
		{
			if (_library == null || _library.RenderMesh == null || _library.Material == null)
				return;

			if (!_library.TryGetClip(_clip, out var clip))
				return;

			float duration = Mathf.Max(0.01f, clip.FrameCount / Mathf.Max(1f, _library.BakeFps));

			for (int i = 0; i < _count; i++)
			{
				float nt = Time.time / duration + _timeOffsets[i];
				_frame[i] = _library.GetFrame(clip, nt);
			}

			var shadows = _castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;

			for (int start = 0; start < _count; start += BatchSize)
			{
				int batch = Mathf.Min(BatchSize, _count - start);
				System.Array.Copy(_matrices, start, _batchMatrices, 0, batch);
				System.Array.Copy(_frame, start, _batchFrame, 0, batch);
				System.Array.Copy(_colors, start, _batchColors, 0, batch);

				_mpb.Clear();
				// VAT layout is constant per library but must ride on the MPB so it can't be lost to
				// stale material serialization. Same values for every instance in the batch.
				_mpb.SetFloat(_vatWId, _library.TextureWidth);
				_mpb.SetFloat(_vatHId, _library.TextureHeight);
				_mpb.SetFloat(_vatVcId, _library.VertexCount);
				_mpb.SetFloatArray(_frameId, _batchFrame);
				_mpb.SetVectorArray(_colorId, _batchColors);

				if (_verbose && !_loggedFirstDraw)
				{
					_loggedFirstDraw = true;
					Debug.Log($"[CrowdTester] DrawMeshInstanced batch={batch} frame[0]={_batchFrame[0]} pos[0]={_batchMatrices[0].GetColumn(3)}", this);
				}

				Graphics.DrawMeshInstanced(
					_library.RenderMesh, 0, _library.Material,
					_batchMatrices, batch, _mpb,
					shadows, true, gameObject.layer);
			}
		}
	}
}
