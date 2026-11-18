using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Decal : MonoBehaviour
{
	private string _id;
	private DecalProjector _projector;
	public string Id => _id;

	public void Initialize(DecalConfig config)
	{
		_id = config.Id;
		_projector = GetComponent<DecalProjector>();
		if (_projector == null)
		{
			throw new NullReferenceException("Failed to find decal projector in prefab");
		}
		_projector.material = config.GetMaterial();
	}

	/// <summary>
	/// Applies a random variation (atlas cell, rotation, scale) on spawn.
	/// Called on every pop from the pool, not only on creation.
	/// If <paramref name="alignToDirection"/> — the random rotation is skipped, to
	/// keep the orientation along the given direction (e.g. along the bullet trajectory).
	/// </summary>
	public void ApplyVariation(DecalConfig config, bool alignToDirection = false, float scale = 1f)
	{
		if (_projector == null)
			_projector = GetComponent<DecalProjector>();

		if (config.TryGetRandomCell(out var uvScale, out var uvBias))
		{
			_projector.uvScale = uvScale;
			_projector.uvBias = uvBias;
		}

		transform.localScale = Vector3.one * config.Size * config.GetScaleJitter() * (scale > 0f ? scale : 1f);

		if (config.RandomRoll && !alignToDirection)
			transform.Rotate(0f, 0f, UnityEngine.Random.Range(0f, 360f), Space.Self);
	}

	public void Show()
	{
		gameObject.SetActive(true);
	}

	public void Hide()
	{
		gameObject.SetActive(false);
	}
}
