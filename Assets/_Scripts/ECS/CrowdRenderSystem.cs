using System.Collections.Generic;
using Leopotam.EcsLite;
using Scene.Animation;
using UnityEngine;
using UnityEngine.Rendering;

namespace ECS
{
	/// <summary>
	/// Draws crowd mobs (those with a <see cref="CrowdInstanceComponent"/>) as GPU-instanced Vertex
	/// Animation Textures instead of SkinnedMeshRenderers. Each frame it reconciles the mob's requested
	/// <see cref="AnimationStateComponent"/> into a baked clip, advances playback time, computes the VAT
	/// frame, and batches instances per <see cref="CrowdAnimationLibrary"/> into DrawMeshInstanced calls.
	///
	/// Runs after MoveSystem/AnimationSystem so mob transforms are up to date for this frame.
	/// </summary>
	public class CrowdRenderSystem : IEcsRunSystem
	{
		private const int BatchSize = 1023; // Graphics.DrawMeshInstanced hard limit per call

		private static readonly int FrameId = Shader.PropertyToID("_Frame");
		private static readonly int ColorId = Shader.PropertyToID("_InstColor");
		private static readonly int VatWId = Shader.PropertyToID("_VatWidth");
		private static readonly int VatHId = Shader.PropertyToID("_VatHeight");
		private static readonly int VatVcId = Shader.PropertyToID("_VatVertexCount");

		private readonly Dictionary<CrowdAnimationLibrary, Batch> _batches = new();
		private readonly Matrix4x4[] _drawMatrices = new Matrix4x4[BatchSize];
		private readonly float[] _drawFrames = new float[BatchSize];
		private readonly Vector4[] _drawColors = new Vector4[BatchSize];
		private MaterialPropertyBlock _mpb;

		public CrowdRenderSystem()
		{
			// _InstColor defaults to 0 (=> black) if never set. Each frame we overwrite [0..n) from the
			// batch's per-instance tints (MobConfig.Tint); this white seed just keeps any untouched tail
			// slots harmless.
			for (int i = 0; i < _drawColors.Length; i++)
				_drawColors[i] = Vector4.one;
		}

		private sealed class Batch
		{
			public readonly List<Matrix4x4> Matrices = new();
			public readonly List<float> Frames = new();
			public readonly List<Vector4> Colors = new();

			public void Clear()
			{
				Matrices.Clear();
				Frames.Clear();
				Colors.Clear();
			}
		}

		public void Run(IEcsSystems systems)
		{
			var world = systems.GetWorld();
			var mobPool = world.GetPool<MobComponent>();
			var crowdPool = world.GetPool<CrowdInstanceComponent>();
			var animPool = world.GetPool<AnimationStateComponent>();

			foreach (var batch in _batches.Values)
				batch.Clear();

			float dt = Time.deltaTime;

			foreach (var entity in world.Filter<MobComponent>().Inc<CrowdInstanceComponent>().End())
			{
				ref var mob = ref mobPool.Get(entity);
				if (mob.Value == null || !mob.Value.gameObject.activeSelf)
					continue;

				ref var crowd = ref crowdPool.Get(entity);
				var library = crowd.Library;
				if (library == null || library.RenderMesh == null || library.Material == null)
					continue;

				// Reconcile requested animation -> current clip; reset playback time when the clip changes.
				AnimationType requested = animPool.Has(entity) ? animPool.Get(entity).Requested : AnimationType.Run;
				if (!crowd.Initialized || crowd.CurrentClip != requested)
				{
					crowd.CurrentClip = requested;
					crowd.ClipTime = 0f;
					crowd.Initialized = true;
				}
				else
				{
					crowd.ClipTime += dt;
				}

				if (!library.TryGetClip(crowd.CurrentClip, out var clip))
					continue;

				float duration = Mathf.Max(0.01f, clip.FrameCount / Mathf.Max(1f, library.BakeFps));
				float frame = library.GetFrame(clip, crowd.ClipTime / duration);

				if (!_batches.TryGetValue(library, out var b))
				{
					b = new Batch();
					_batches[library] = b;
				}

				b.Matrices.Add(mob.Value.transform.localToWorldMatrix);
				b.Frames.Add(frame);
				// Per-config tint (MobConfig.Tint). White => unchanged. Zero (unset) would be black,
				// but MobSpawnSystem always seeds Tint from the config, which defaults to white.
				b.Colors.Add(crowd.Tint);
			}

			_mpb ??= new MaterialPropertyBlock();

			foreach (var kv in _batches)
			{
				var library = kv.Key;
				var b = kv.Value;
				int count = b.Matrices.Count;

				for (int start = 0; start < count; start += BatchSize)
				{
					int n = Mathf.Min(BatchSize, count - start);
					b.Matrices.CopyTo(start, _drawMatrices, 0, n);
					b.Frames.CopyTo(start, _drawFrames, 0, n);
					b.Colors.CopyTo(start, _drawColors, 0, n);

					_mpb.Clear();
					_mpb.SetFloat(VatWId, library.TextureWidth);
					_mpb.SetFloat(VatHId, library.TextureHeight);
					_mpb.SetFloat(VatVcId, library.VertexCount);
					_mpb.SetFloatArray(FrameId, _drawFrames);
					_mpb.SetVectorArray(ColorId, _drawColors);

					Graphics.DrawMeshInstanced(
						library.RenderMesh, 0, library.Material,
						_drawMatrices, n, _mpb,
						ShadowCastingMode.On, true);
				}
			}
		}
	}
}
