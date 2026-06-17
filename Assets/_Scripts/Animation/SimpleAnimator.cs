using System;
using System.Collections.Generic;
using Scene.Animation;
using UnityEngine;

[Serializable]
public struct Frame
{
	public Sprite Sprite;
	public bool FlipX;
	public bool FlipY;
}

[RequireComponent(typeof(Animator))]
public class SimpleAnimator : MonoBehaviour
{
	[Header("Animator")]
	[SerializeField] private Animator _animator; // TODO: Move to DI / auto-assign

	[Header("Settings")]
	[SerializeField] private string _defaultAnimationName = "idle"; // state name in the Animator

	// Cache string->hash once per id so we don't call Animator.StringToHash on every set.
	// (The AnimationType overload bypasses this entirely via precomputed hashes.)
	private static readonly Dictionary<string, int> _hashCache = new();

	private bool _isPaused;
	private int _currentHash;
	private bool _hasCurrent;

	public bool HasActiveAnimation => _hasCurrent && !_isPaused;

	private void Awake()
	{
		if (_animator == null)
			_animator = GetComponent<Animator>();

		// Off-screen mobs skip bone/transform writes. Cheap, big win for crowds.
		// (Gameplay logic doesn't read Animator state, so CullCompletely is an option too
		//  if profiling shows this is still hot.)
		if (_animator != null)
			_animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
	}

	private void OnDisable()
	{
		_hasCurrent = false;
	}

	public void SetDefaultAnimation()
	{
		if (!string.IsNullOrEmpty(_defaultAnimationName))
			SetAnimation(_defaultAnimationName);
	}

	/// <summary>
	/// Plays an animation by its <see cref="AnimationType"/> using a precomputed hash (no string work).
	/// Preferred path for mobs driven by the ECS reconciliation loop.
	/// </summary>
	public void SetAnimation(AnimationType type, float crossFadeTime = 0.1f)
	{
		PlayHash(type.ToHash(), crossFadeTime);
	}

	/// <summary>
	/// Plays an animation by its Animator state name. Kept for callers that still use string ids.
	/// </summary>
	public void SetAnimation(string id, float crossFadeTime = 0.1f)
	{
		if (string.IsNullOrEmpty(id))
		{
			Debug.LogWarning($"[{nameof(SimpleAnimator)}] Empty animation id on {name}");
			return;
		}

		PlayHash(GetHash(id), crossFadeTime);
	}

	private void PlayHash(int stateHash, float crossFadeTime)
	{
		if (_animator == null)
		{
			Debug.LogWarning($"[{nameof(SimpleAnimator)}] Animator is null on {name}");
			return;
		}

		// Already playing this state and not paused — nothing to do. Avoids a redundant
		// CrossFade restart when callers re-request the same animation every frame.
		if (_hasCurrent && stateHash == _currentHash && !_isPaused)
			return;

		_currentHash = stateHash;
		_hasCurrent = true;
		_isPaused = false;
		_animator.speed = 1f;

		_animator.CrossFade(stateHash, crossFadeTime, 0);
	}

	/// <summary>
	/// Stops the current animation (clears the current state).
	/// </summary>
	public void Stop()
	{
		_hasCurrent = false;
	}

	/// <summary>
	/// Pauses the animation in place.
	/// </summary>
	public void Pause()
	{
		_isPaused = true;

		if (_animator != null)
			_animator.speed = 0f;
	}

	/// <summary>
	/// Resumes playback at normal speed.
	/// </summary>
	public void Play()
	{
		_isPaused = false;

		if (_animator != null)
			_animator.speed = 1f;
	}

	private static int GetHash(string id)
	{
		if (!_hashCache.TryGetValue(id, out var hash))
		{
			hash = Animator.StringToHash(id);
			_hashCache[id] = hash;
		}

		return hash;
	}
}
