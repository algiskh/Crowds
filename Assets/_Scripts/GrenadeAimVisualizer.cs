using UnityEngine;

/// <summary>
/// Визуализатор предполагаемой точки попадания броска гранаты.
/// Аналог AimVisualizer для оружия, но показывает место приземления/взрыва.
/// Драйвится из GrenadeThrowSystem: Show во время зарядки, Hide после броска.
/// </summary>
public class GrenadeAimVisualizer : MonoBehaviour
{
	[SerializeField] private GameObject _root;            // контейнер маркера (вкл/выкл)
	[SerializeField] private Transform _marker;           // наземный маркер (кольцо/декаль)
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
