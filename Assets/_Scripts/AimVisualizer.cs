using UnityEngine;

public class AimVisualizer : MonoBehaviour
{
	[SerializeField] private AimType _aimType;
	[SerializeField] private Transform _root;
	[SerializeField] private Transform _aim;
	[SerializeField] private Transform _lookerTarget;
	[SerializeField] private float _maxDistance;

	private Camera _camera;
	private void Awake()
	{
		_camera = Camera.main;
		if (_camera == null)
		{
			Debug.LogError("Main camera not found. Please ensure there is a camera tagged as 'MainCamera'.");
		}
	}

	public bool TryToGetLooker(out LookerAtCamera looker)
	{
		looker = default;
		if (_lookerTarget == null)
			return false;

		looker.Transform = _lookerTarget;
		looker.FlatBillboard = true;
		return true;
	}

	public void SetAim(Vector2 screenPosition)
	{
		if (_camera == null || _root == null || _aim == null)
			return;

		float rootY = _root.position.y;

		Ray ray = _camera.ScreenPointToRay(new Vector3(screenPosition.x, screenPosition.y, 0f));

		Plane plane = new Plane(Vector3.up, new Vector3(0f, rootY, 0f));

		Vector3 target;
		if (plane.Raycast(ray, out float enter))
		{
			target = ray.GetPoint(enter);
		}
		else
		{
			float zAtRoot = _camera.WorldToScreenPoint(_root.position).z;
			target = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, zAtRoot));
		}

		target.y = rootY;

		Vector3 fromRoot = target - _root.position;
		float maxDist = Mathf.Max(0f, _maxDistance);
		if (fromRoot.sqrMagnitude > maxDist * maxDist)
		{
			target = _root.position + fromRoot.normalized * maxDist;
			target.y = rootY; 
		}

		_aim.position = target;
	}
}
