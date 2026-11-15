using UnityEngine;

/// <summary>
/// Visualizer of the expected grenade-throw landing point.
/// Analogous to the weapon AimVisualizer, but shows the landing/explosion spot.
/// Driven from GrenadeThrowSystem: Show while charging, Hide after the throw.
/// </summary>
public class GrenadeAimVisualizer : MonoBehaviour
{
	[SerializeField] private GameObject _root;            // marker container (on/off)
	[SerializeField] private Transform _marker;           // ground marker (ring/decal)
	[SerializeField] private bool _scaleMarkerToRadius = true;

	public void Show(Vector3 worldPosition, float radius)
	{
		if (_root != null && !_root.activeSelf)
			_root.SetActive(true);

		if (_marker != null)
		{
			_marker.position = worldPosition;
			if (_scaleMarkerToRadius)
			{
				float diameter = radius * 2f;
				_marker.localScale = new Vector3(diameter, _marker.localScale.y, diameter);
			}
		}
	}

	public void Hide()
	{
		if (_root != null && _root.activeSelf)
			_root.SetActive(false);
	}
}
