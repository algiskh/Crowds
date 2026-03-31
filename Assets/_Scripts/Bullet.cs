using UnityEngine;

public class Bullet : MonoBehaviour
{
	[SerializeField] private int _maxPierceCount = 1;
	public int MaxPierceCount => _maxPierceCount;

	private TrailRenderer _trail;
	private void Awake()
	{
		_trail = GetComponent<TrailRenderer>();
	}

	private void OnEnable()
	{
		
	}

	private void OnDisable()
	{
		if (_trail != null)
		{
			_trail.Clear();
		}
	}
}
