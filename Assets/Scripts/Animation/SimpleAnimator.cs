using System.Linq;
using System;
using UnityEngine;
using System.Collections;

[Serializable]
public class Animation
{
	public string Id;
	public int FPS = 8;
	public bool Loop = true;
	public Frame[] Frames;
}

[Serializable]
public struct Frame
{
	public Sprite Sprite;
	public bool FlipX;
	public bool FlipY;
}

public class SimpleAnimator : MonoBehaviour
{
	[SerializeField] private Animation[] _animations;
	[SerializeField] private SpriteRenderer _image;

	private Animation _defaultAnimation;
	private bool _useCustomFlip;
	private Coroutine _animationCoroutine;
	private bool _isPaused;
	private string _currentAnimationId;

	public void SetFlipX(bool flip)
	{
		_useCustomFlip = true;
		_image.flipX = flip;
	}

	public void SetDefaultAnimation()
	{
		if (_defaultAnimation != null)
		{
			SetAnimation(_defaultAnimation.Id);
		}
		else if (_animations.Length > 0)
		{
			_defaultAnimation = _animations[0];
			SetAnimation(_defaultAnimation.Id);
		}
	}

	public void SetAnimation(string id, Action onComplete = null)
	{
		if (_image == null) return;

		if (_currentAnimationId == id && !_isPaused)
			return;

		_currentAnimationId = id;
		_isPaused = false;

		if (_animationCoroutine != null)
			StopCoroutine(_animationCoroutine);

		var animation = _animations.FirstOrDefault(a => a.Id == id);
		if (animation == null || animation.Frames == null || animation.Frames.Length == 0)
		{
			Debug.LogWarning($"Animation '{id}' not found or has no frames!");
			return;
		}

		_animationCoroutine = StartCoroutine(PlayAnimation(animation, onComplete));
	}

	public void Stop()
	{
		if (_animationCoroutine != null)
		{
			StopCoroutine(_animationCoroutine);
		}
		_currentAnimationId = null;
	}

	public void Pause()
	{
		_isPaused = true;
	}

	public void Play()
	{
		if (_animationCoroutine == null)
		{
			SetDefaultAnimation();
		}
		_isPaused = false;
	}

	private void OnDestroy()
	{
		Stop();
	}

	private IEnumerator PlayAnimation(Animation animation, Action onComplete)
	{
		float frameDuration = 1f / animation.FPS;

		do
		{
			foreach (var frame in animation.Frames)
			{
				if (_image == null)
					yield break;

				_image.sprite = frame.Sprite;
				if (!_useCustomFlip)
				{
					_image.flipX = frame.FlipX;
					_image.flipY = frame.FlipY;
				}

				float elapsed = 0f;
				while (elapsed < frameDuration)
				{
					if (!_isPaused)
						elapsed += Time.deltaTime;
					yield return null;
				}
			}
		}
		while (animation.Loop);

		onComplete?.Invoke();
	}

}
