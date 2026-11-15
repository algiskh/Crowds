using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A door between level areas. Closed: a carving NavMeshObstacle cuts the opening out of the navmesh, so neither
/// the player (kept on the navmesh by its NavMeshAgent) nor mobs (pathfinding) can pass, and a door panel fills it.
/// Opening: the panel sinks into the floor over <see cref="_openTime"/>, then the obstacle is switched off and the
/// navmesh reconnects (carving updates asynchronously, no rebake).
///
/// Opened by MissionSystem either at the start of a difficulty stage (<see cref="OpenOnStage"/>) or when the player
/// comes within <see cref="_keyOpenRadius"/> holding the key <see cref="RequiredKey"/>. GateOpenedCondition watches it.
/// The prefab root should carry a NavMeshModifier "Ignore From Build" (the closed panel must not block the bake).
/// Optionally it shuts again for good once the player is deep enough in the next area
/// (<see cref="CloseWhenPlayerInZone"/>), so the way back is closed.
/// See Docs/KeyLevelFeature.md.
/// </summary>
public sealed class LevelGate : MonoBehaviour
{
	public enum OpenMode { OnStage, WithKey, Manual }

	[SerializeField, BoxGroup("Gate")] private string _id = "gate";
	[SerializeField, BoxGroup("Gate")] private OpenMode _openMode = OpenMode.OnStage;
	[SerializeField, BoxGroup("Gate"), ShowIf(nameof(_openMode), OpenMode.OnStage)]
	private DifficultyLevel _openOnStage = DifficultyLevel.level2;
	[SerializeField, BoxGroup("Gate"), ShowIf(nameof(_openMode), OpenMode.WithKey)]
	private string _requiredKey = "key";
	[SerializeField, BoxGroup("Gate"), ShowIf(nameof(_openMode), OpenMode.WithKey), MinValue(0.5f),
	 Tooltip("Player distance (XZ, from the gate centre) at which a carried key opens the gate.")]
	private float _keyOpenRadius = 3.5f;
	[SerializeField, BoxGroup("Gate"), Tooltip("Shown in the UI log when the gate opens. Empty = silent.")]
	private string _openMessage = "Проход открыт";
	[SerializeField, BoxGroup("Gate"),
	 Tooltip("Once open, the gate shuts again for good when the player is inside this LevelZone (and at least " +
	         "CloseMinDistance away from the gate) — 'the way back is closed'. Empty = stays open.")]
	private string _closeWhenPlayerInZone = string.Empty;
	[SerializeField, BoxGroup("Gate"), MinValue(0f), HideIf("@string.IsNullOrEmpty(_closeWhenPlayerInZone)")]
	private float _closeMinDistance = 4f;
	[SerializeField, BoxGroup("Gate"), HideIf("@string.IsNullOrEmpty(_closeWhenPlayerInZone)")]
	private string _closeMessage = string.Empty;

	[SerializeField, BoxGroup("View"), Required] private Transform _panel;
	[SerializeField, BoxGroup("View")] private NavMeshObstacle _obstacle;
	[SerializeField, BoxGroup("View"), MinValue(0.05f)] private float _openTime = 1f;
	[SerializeField, BoxGroup("View"), Tooltip("How far the panel sinks when open.")] private float _sinkDepth = 3f;
	[SerializeField, BoxGroup("View"), Tooltip("Optional light/marker shown while the gate waits for its key.")]
	private GameObject _lockedIndicator;

	private Vector3 _panelClosedPos;
	private float _openProgress = -1f; // <0 = closed, 0..1 = opening, 1 = open
	private bool _closing;             // re-closing: _openProgress runs back 1 → 0, then the gate is shut for good
	private bool _sealed;

	public string Id => _id;
	public OpenMode Mode => _openMode;
	public DifficultyLevel OpenOnStage => _openOnStage;
	public string RequiredKey => _requiredKey;
	public float KeyOpenRadius => _keyOpenRadius;
	public string OpenMessage => _openMessage;
	public string CloseWhenPlayerInZone => _closeWhenPlayerInZone;
	public float CloseMinDistance => _closeMinDistance;
	public string CloseMessage => _closeMessage;
	/// <summary>Open or opening (a re-closing / re-closed gate is not open).</summary>
	public bool IsOpen => _openProgress >= 0f && !_closing && !_sealed;
	/// <summary>Was opened and has been (or is being) closed again — never reopens.</summary>
	public bool IsSealed => _closing || _sealed;

	private void Awake()
	{
		if (_panel != null)
			_panelClosedPos = _panel.localPosition;
		if (_obstacle != null)
		{
			_obstacle.carving = true;
			_obstacle.carveOnlyStationary = false;
			_obstacle.enabled = true;
		}
	}

	/// <summary>Starts opening (idempotent). The obstacle goes away right away so mobs can path through.</summary>
	public void Open()
	{
		if (IsOpen || IsSealed)
			return;
		_openProgress = 0f;
		if (_obstacle != null)
			_obstacle.enabled = false;
		if (_lockedIndicator != null)
			_lockedIndicator.SetActive(false);
	}

	/// <summary>
	/// Shuts an open gate for good (idempotent): the obstacle comes back at once (mobs path away, the player can't
	/// step in), the panel rises over OpenTime.
	/// </summary>
	public void Close()
	{
		if (!IsOpen)
			return;
		_closing = true;
		if (_obstacle != null)
			_obstacle.enabled = true;
		if (_panel != null)
			_panel.gameObject.SetActive(true);
	}

	/// <summary>Animates the panel (call every unpaused frame).</summary>
	public void Tick(float dt)
	{
		if (_panel == null || _openProgress < 0f)
			return;
		if (_closing)
		{
			_openProgress = Mathf.Max(0f, _openProgress - dt / _openTime);
			if (_openProgress <= 0f)
			{
				_closing = false;
				_sealed = true;
			}
		}
		else
		{
			if (_sealed || _openProgress >= 1f)
				return;
			_openProgress = Mathf.Min(1f, _openProgress + dt / _openTime);
		}
		float t = Mathf.SmoothStep(0f, 1f, _openProgress);
		_panel.localPosition = _panelClosedPos + Vector3.down * (_sinkDepth * t);
		if (_openProgress >= 1f && !_closing)
			_panel.gameObject.SetActive(false);
	}

	public void SetLockedIndicator(bool visible)
	{
		if (_lockedIndicator != null)
			_lockedIndicator.SetActive(visible && !IsOpen);
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Gizmos.color = IsOpen ? Color.green : new Color(1f, 0.5f, 0f);
		Gizmos.DrawWireCube(transform.position + Vector3.up, new Vector3(1f, 2f, 1f));
		if (_openMode == OpenMode.WithKey)
		{
			UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 0.6f);
			UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _keyOpenRadius);
		}
	}
#endif
}
