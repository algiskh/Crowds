using System;
using System.Collections.Generic;
using UnityEngine;

namespace Scene.Animation
{
	/// <summary>
	/// One baked animation clip inside a <see cref="CrowdAnimationLibrary"/>.
	/// Frames are rows in the VAT texture: rows [StartFrame .. StartFrame+FrameCount-1].
	/// </summary>
	[Serializable]
	public struct CrowdClip
	{
		public AnimationType Type;
		public int StartFrame;   // first row in the VAT texture
		public int FrameCount;   // number of rows this clip occupies
		public bool Loop;        // looping (idle/run) vs one-shot (die/attack)
	}

	/// <summary>
	/// Baked Vertex-Animation-Texture data for a single mob visual. Produced by the editor
	/// baker (VatBakerWindow) and consumed by the VAT shader + crowd renderer. Replaces the
	/// SkinnedMeshRenderer+Animator for GPU-instanced crowds: the render mesh is drawn with
	/// <see cref="Material"/>, and the vertex shader reads the pose from <see cref="PositionMap"/>.
	///
	/// Texture layout: width = vertexCount (one column per vertex), height = total baked frames
	/// (rows), all clips stacked. Data is object-space, in the mob-root local frame, RGBAHalf/linear.
	/// </summary>
	[CreateAssetMenu(fileName = "CrowdAnimationLibrary", menuName = "Scriptable Objects/Crowd Animation Library", order = 2)]
	public class CrowdAnimationLibrary : ScriptableObject
	{
		[Header("Baked assets")]
		[SerializeField] private Mesh _renderMesh;      // static mesh, uv2.x carries the vertex column
		[SerializeField] private Texture2D _positionMap; // RGBAHalf, object-space positions
		[SerializeField] private Texture2D _normalMap;   // RGBAHalf, object-space normals (optional)
		[SerializeField] private Material _material;     // instance of the VAT shader wired to the maps

		[Header("Layout")]
		[SerializeField] private int _vertexCount;   // vertices per frame
		[SerializeField] private int _frameCount;    // total frames (slots) across all clips
		[SerializeField] private int _textureWidth;  // VAT texture width (data is flat-wrapped into it)
		[SerializeField] private int _textureHeight; // VAT texture height
		[SerializeField] private float _bakeFps = 30f;

		[SerializeField] private List<CrowdClip> _clips = new();

		public Mesh RenderMesh => _renderMesh;
		public Texture2D PositionMap => _positionMap;
		public Texture2D NormalMap => _normalMap;
		public Material Material => _material;
		public int VertexCount => _vertexCount;
		public int FrameCount => _frameCount;
		public int TextureWidth => _textureWidth;
		public int TextureHeight => _textureHeight;
		public float BakeFps => _bakeFps;
		public IReadOnlyList<CrowdClip> Clips => _clips;

		/// <summary>Finds a baked clip for the given animation type. Falls back to the first clip.</summary>
		public bool TryGetClip(AnimationType type, out CrowdClip clip)
		{
			for (int i = 0; i < _clips.Count; i++)
			{
				if (_clips[i].Type == type)
				{
					clip = _clips[i];
					return true;
				}
			}

			if (_clips.Count > 0)
			{
				clip = _clips[0];
				return true;
			}

			clip = default;
			return false;
		}

		/// <summary>
		/// Absolute frame-slot index for a clip at a normalized time [0..1], fed to the shader per instance
		/// as <c>_Frame</c>. The shader turns it into a texel via linear = frame*vertexCount + vertexId.
		/// Looping clips wrap; one-shots clamp to the last frame.
		/// </summary>
		public float GetFrame(in CrowdClip clip, float normalizedTime)
		{
			int frames = Mathf.Max(1, clip.FrameCount);
			int frameIndex;

			if (clip.Loop)
				frameIndex = Mathf.FloorToInt(Mathf.Repeat(normalizedTime, 1f) * frames) % frames;
			else
				frameIndex = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(normalizedTime) * frames), 0, frames - 1);

			return clip.StartFrame + frameIndex;
		}

#if UNITY_EDITOR
		/// <summary>Editor-only: called by the baker to populate the asset after baking.</summary>
		public void EditorAssign(Mesh mesh, Texture2D positionMap, Texture2D normalMap, Material material,
			int vertexCount, int frameCount, int textureWidth, int textureHeight, float bakeFps, List<CrowdClip> clips)
		{
			_renderMesh = mesh;
			_positionMap = positionMap;
			_normalMap = normalMap;
			_material = material;
			_vertexCount = vertexCount;
			_frameCount = frameCount;
			_textureWidth = textureWidth;
			_textureHeight = textureHeight;
			_bakeFps = bakeFps;
			_clips = clips;
		}
#endif
	}
}
