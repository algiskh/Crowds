using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The on-screen log: a column of <see cref="UILogSlot"/> lines under a VerticalLayoutGroup. Newest
/// entries are inserted on top (LIFO); each slot expires on its own timer. Slots are pooled. When the
/// active count would exceed <see cref="_maxSlots"/>, the oldest is evicted immediately.
/// Driven from ECS via <c>UILogSystem</c> calling <see cref="AddEntry(string)"/>.
/// </summary>
public class UILogView : MonoBehaviour
{
	[Tooltip("Parent for the slots — the object with the VerticalLayoutGroup. New slots are added here.")]
	[SerializeField] private Transform _container;
	[SerializeField] private UILogSlot _slotPrefab;
	[SerializeField, Min(0f)] private float _defaultLifetime = 5f;
	[SerializeField, Min(0f)] private float _fadeDuration = 0.5f;
	[SerializeField, Min(1)] private int _maxSlots = 6;

	private readonly Stack<UILogSlot> _pool = new();
	private readonly List<UILogSlot> _active = new(); // [0] = oldest, last = newest

	public void AddEntry(string message) => AddEntry(message, _defaultLifetime);

	public void AddEntry(string message, float lifetime)
	{
		if (_slotPrefab == null || _container == null)
		{
			Debug.LogWarning("UILogView: _slotPrefab or _container is not assigned.", this);
			return;
		}

		// Keep room for the newcomer: evict the oldest while at capacity (ForceFinish recycles synchronously).
		while (_active.Count >= _maxSlots && _active.Count > 0)
			_active[0].ForceFinish();

		var slot = _pool.Count > 0 ? _pool.Pop() : Instantiate(_slotPrefab, _container);
		slot.transform.SetAsFirstSibling(); // newest on top
		_active.Add(slot);
		slot.Show(message, lifetime, _fadeDuration, Recycle);
	}

	private void Recycle(UILogSlot slot)
	{
		_active.Remove(slot);
		slot.gameObject.SetActive(false);
		_pool.Push(slot);
	}
}
