using UnityEngine;

public class Bullet : MonoBehaviour
{
	private TrailRenderer _trail;
	private void Awake()
	{
		_trail = GetComponent<TrailRenderer>();
	}

	private void OnDisable()
	{
		if (_trail != null)
		{
			_trail.Clear();
		}
	}
}
