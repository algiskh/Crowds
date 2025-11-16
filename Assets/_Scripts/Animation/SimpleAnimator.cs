using System;
using System.Collections;
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
	[SerializeField] private string _defaultAnimationName = "idle"; // Имя стейта в Animator

	private bool _isPaused;
	private string _currentAnimationId;
	private Coroutine _onCompleteRoutine;

	public bool HasActiveAnimation => !string.IsNullOrEmpty(_currentAnimationId) && !_isPaused;

	private void Awake()
	{
		if (_animator == null)
			_animator = GetComponent<Animator>();
	}

	private void OnEnable()
	{
	}

	private void OnDisable()
	{
		_currentAnimationId = null;
	}

	public void SetDefaultAnimation()
	{
		if (!string.IsNullOrEmpty(_defaultAnimationName))
		{
			SetAnimation(_defaultAnimationName);
		}
	}

	public void SetAnimation(string id, Action onComplete = null, float crossFadeTime = 0.1f)
	{
		if (_animator == null)
		{
			Debug.LogWarning($"[{nameof(SimpleAnimator)}] Animator is null on {name}");
			return;
		}

		if (string.IsNullOrEmpty(id))
		{
			Debug.LogWarning($"[{nameof(SimpleAnimator)}] Empty animation id on {name}");
			return;
		}

		_currentAnimationId = id;
		_isPaused = false;
		_animator.speed = 1f;

		int stateHash = Animator.StringToHash(id);

		// Запускаем стейт. Считаем, что стейт на 0-м слое.
		_animator.CrossFade(stateHash, crossFadeTime, 0);

		// Если раньше была корутина onComplete — гасим её
		if (_onCompleteRoutine != null)
		{
			StopCoroutine(_onCompleteRoutine);
			_onCompleteRoutine = null;
		}

		if (onComplete != null)
		{
			_onCompleteRoutine = StartCoroutine(WaitForAnimationEnd(stateHash, onComplete));
		}

		Debug.Log($"SetAnimation '{id}' on {GetHashCode()}");
	}

	/// <summary>
	/// Остановить анимацию (сбросить текущий id и отменить onComplete).
	/// </summary>
	public void Stop()
	{
		_currentAnimationId = null;

		if (_onCompleteRoutine != null)
		{
			StopCoroutine(_onCompleteRoutine);
			_onCompleteRoutine = null;
		}

		// Можно либо оставить текущий кадр,
		// либо вернуться к дефолтному стейту:
		// SetDefaultAnimation();
	}

	/// <summary>
	/// Поставить анимацию на паузу.
	/// </summary>
	public void Pause()
	{
		_isPaused = true;

		if (_animator != null)
			_animator.speed = 0f;
	}

	/// <summary>
	/// Снять паузу и продолжить проигрывание текущего стейта.
	/// </summary>
	public void Play()
	{
		_isPaused = false;

		if (_animator != null)
			_animator.speed = 1f;
	}

	private IEnumerator WaitForAnimationEnd(int stateHash, Action onComplete)
	{
		// Ждём, пока Animator реально войдёт в нужный стейт
		int layer = 0;
		AnimatorStateInfo info = _animator.GetCurrentAnimatorStateInfo(layer);

		// Если кроссфейд длинный или стейты накладываются — может занять пару кадров
		while (info.shortNameHash != stateHash)
		{
			yield return null;

			if (_animator == null)
				yield break;

			info = _animator.GetCurrentAnimatorStateInfo(layer);
		}

		// Если стейт зациклен — ждём одну его длину и вызываем onComplete
		if (info.loop)
		{
			yield return new WaitForSeconds(info.length);
		}
		else
		{
			// Для нелупящихся — ждём, пока normalizedTime >= 1
			while (info.shortNameHash == stateHash && info.normalizedTime < 1f)
			{
				yield return null;

				if (_animator == null)
					yield break;

				info = _animator.GetCurrentAnimatorStateInfo(layer);
			}
		}

		_onCompleteRoutine = null;
		onComplete?.Invoke();
	}

	private void OnDestroy()
	{
		Stop();
	}
}
