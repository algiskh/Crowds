using UnityEngine;

/// <summary>
/// Scene hint arrow for scripted levels (the tutorial). Shown only during the listed difficulty stages: MissionSystem
/// calls <see cref="ApplyStage"/> at every stage start (the object is switched on/off, so a hidden arrow costs
/// nothing). Two looks, both animated here without any ECS state:
///  • <see cref="Style.Floor"/> — a flat chevron lying on the floor, pointing along its forward (+Z). Chains of them
///    pulse one after another (<see cref="_phase"/>), so the eye follows the direction;
///  • <see cref="Style.Marker"/> — a big arrow floating above a target and pointing down at it, bobbing and turning.
/// Visual meshes live under <see cref="_visual"/> (built by TutorialLevelBuilder). See Docs/TutorialLevelFeature.md.
/// </summary>
public sealed class TutorialArrow : MonoBehaviour
{
	public enum Style
	{
		Floor,
		Marker,
	}

	[SerializeField, Tooltip("Stages during which the arrow is visible.")]
	private DifficultyLevel[] _stages = System.Array.Empty<DifficultyLevel>();
	[SerializeField] private Style _style = Style.Floor;
	[SerializeField, Tooltip("Animated child (the meshes).")]
	private Transform _visual;
	[SerializeField, Tooltip("Animation offset in seconds: chevrons of one chain get increasing phases.")]
	private float _phase;
	[SerializeField, Min(0.1f), Tooltip("Pulse / bob cycles per second.")]
	private float _speed = 1.2f;
	[SerializeField, Min(0f), Tooltip("Floor: forward slide of the pulse, m. Marker: bob height, m.")]
	private float _amplitude = 0.35f;

	private Vector3 _basePosition;
	private Vector3 _baseScale;
	private bool _cached;

	public bool IsShownOn(DifficultyLevel stage)
	{
		if (_stages == null)
			return false;
		for (int i = 0; i < _stages.Length; i++)
			if (_stages[i] == stage)
				return true;
		return false;
	}

	/// <summary>Show / hide for the stage that has just started.</summary>
	public void ApplyStage(DifficultyLevel stage)
	{
		bool show = IsShownOn(stage);
		if (gameObject.activeSelf != show)
			gameObject.SetActive(show);
	}

	private void Cache()
	{
		if (_cached || _visual == null)
			return;
		_basePosition = _visual.localPosition;
		_baseScale = _visual.localScale;
		_cached = true;
	}

	private void Update()
	{
		if (_visual == null)
			return;
		Cache();
		float t = (Time.time + _phase) * _speed;
		float wave = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f); // 0..1

		if (_style == Style.Floor)
		{
			// Swell + nudge forward: in a chain with growing phases it reads as a wave running along the arrows.
			_visual.localPosition = _basePosition + Vector3.forward * (_amplitude * wave);
			_visual.localScale = _baseScale * (0.85f + 0.3f * wave);
		}
		else
		{
			_visual.localPosition = _basePosition + Vector3.up * (_amplitude * wave);
			_visual.localRotation = Quaternion.Euler(0f, Time.time * 90f, 0f);
			_visual.localScale = _baseScale * (0.95f + 0.1f * wave);
		}
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		Gizmos.color = new Color(1f, 0.7f, 0.1f, 0.9f);
		var p = transform.position + Vector3.up * 0.1f;
		if (_style == Style.Floor)
			Gizmos.DrawLine(p, p + transform.forward * 1.5f);
		else
			Gizmos.DrawLine(p + Vector3.up * 3f, p);
	}
#endif
}
