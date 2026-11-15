using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class AimVisualizer : MonoBehaviour
{
	[BoxGroup("Aim")]
	[SerializeField] private AimType _aimType;
	[BoxGroup("Aim")]
	[SerializeField] private Transform _root;
	[BoxGroup("Aim")]
	[SerializeField] private Transform _aim;
	[BoxGroup("Aim")]
	[SerializeField] private Transform _lookerTarget;
	[BoxGroup("Aim")]
	[SerializeField] private float _maxDistance;

	[BoxGroup("Ground Decal")]
	[Tooltip("Show the old floor decal on the aim point. Off — only the screen crosshair is shown " +
	         "(the floor marker is then used only for the grenade throw preview).")]
	[SerializeField] private bool _showGroundDecal = false;

	[BoxGroup("Crosshair")]
	[SerializeField] private bool _showCrosshair = true;
	[BoxGroup("Crosshair")]
	[SerializeField] private Color _crosshairColor = new Color(1f, 0.85f, 0.1f, 1f);
	[BoxGroup("Crosshair")]
	[Tooltip("Crosshair size on screen, in pixels.")]
	[SerializeField, Min(4f)] private float _crosshairSize = 62f;
	[BoxGroup("Crosshair")]
	[Tooltip("Ring line thickness, fraction of the crosshair size.")]
	[SerializeField, Range(0.01f, 0.3f)] private float _ringThickness = 0.07f;
	[BoxGroup("Crosshair")]
	[Tooltip("Cross line thickness, fraction of the crosshair size.")]
	[SerializeField, Range(0.01f, 0.3f)] private float _crossThickness = 0.06f;
	[BoxGroup("Crosshair")]
	[Tooltip("Empty gap in the cross center, fraction of the crosshair size.")]
	[SerializeField, Range(0f, 0.8f)] private float _crossGap = 0.25f;

	[BoxGroup("Cursor Dot")]
	[Tooltip("Small dot on the real cursor position (the system cursor is hidden in gameplay).")]
	[SerializeField] private bool _showCursorDot = true;
	[BoxGroup("Cursor Dot")]
	[SerializeField] private Color _cursorDotColor = new Color(1f, 0.85f, 0.1f, 1f);
	[BoxGroup("Cursor Dot")]
	[Tooltip("Dot diameter on screen, in pixels.")]
	[SerializeField, Min(2f)] private float _cursorDotSize = 8f;

	private Camera _camera;
	private AimCrosshair _crosshair;

	private void Awake()
	{
		_camera = Camera.main;
		if (_camera == null)
		{
			Debug.LogError("Main camera not found. Please ensure there is a camera tagged as 'MainCamera'.");
		}

		if (!_showGroundDecal && _aim != null)
		{
			var projector = _aim.GetComponent<DecalProjector>();
			if (projector != null)
				projector.enabled = false;
		}

		if (_showCrosshair)
		{
			_crosshair = new AimCrosshair();
			_crosshair.EnsureCreated(_crosshairColor, _crosshairSize, _ringThickness, _crossThickness, _crossGap);
			if (_showCursorDot)
				_crosshair.EnsureCursorDot(_cursorDotColor, _cursorDotSize);
		}
	}

	private void OnDestroy()
	{
		// AimVisualizerSystem hides the system cursor during gameplay — give it back when leaving the level.
		Cursor.visible = true;
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

	/// <summary>Crosshair and cursor dot together (hidden over pause/fail menus).</summary>
	public void SetCrosshairVisible(bool visible)
	{
		_crosshair?.SetAllVisible(visible);
	}

	public void SetAim(Vector2 screenPosition)
	{
		// The dot follows the real (unclamped) cursor — mouse position or the gamepad virtual cursor.
		_crosshair?.SetCursorPosition(screenPosition);

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

		// The screen crosshair follows the same (distance-clamped) ground point as the decal did.
		if (_crosshair != null)
		{
			Vector3 screen = _camera.WorldToScreenPoint(target);
			if (screen.z > 0f)
				_crosshair.SetScreenPosition(screen);
			else
				_crosshair.SetVisible(false);
		}
	}
}
