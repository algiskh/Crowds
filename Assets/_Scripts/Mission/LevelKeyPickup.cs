using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// A key lying on the level. Hidden until the difficulty stage <see cref="AppearOnStage"/> starts (MissionSystem),
/// then spins/bobs until the player walks within <see cref="PickupRadius"/>; the key id goes into
/// MissionComponent.CollectedKeys (LevelGate with OpenMode.WithKey checks it). See Docs/KeyLevelFeature.md.
/// </summary>
public sealed class LevelKeyPickup : MonoBehaviour
{
	[SerializeField, BoxGroup("Key")] private string _keyId = "key";
	[SerializeField, BoxGroup("Key")] private DifficultyLevel _appearOnStage = DifficultyLevel.level6;
	[SerializeField, BoxGroup("Key"), MinValue(0.3f)] private float _pickupRadius = 1.3f;
	[SerializeField, BoxGroup("Key")] private string _pickupMessage = "Подобран ключ";
	[SerializeField, BoxGroup("Key"), Tooltip("Effect id (EffectsHolder) played where the key appears. Empty = none.")]
	private string _appearEffectId = "collect";

	[SerializeField, BoxGroup("View"), Tooltip("Spinning/bobbing part (the key model). Null = this transform.")]
	private Transform _visual;
	[SerializeField, BoxGroup("View")] private float _spinSpeed = 120f;
	[SerializeField, BoxGroup("View")] private float _bobAmplitude = 0.15f;
	[SerializeField, BoxGroup("View")] private float _bobSpeed = 2.5f;

	private Vector3 _visualBasePos;
	private float _time;

	public string KeyId => _keyId;
	public DifficultyLevel AppearOnStage => _appearOnStage;
	public float PickupRadius => _pickupRadius;
	public string PickupMessage => _pickupMessage;
	public string AppearEffectId => _appearEffectId;
	public bool IsAvailable => gameObject.activeSelf;

	private void Awake()
	{
		if (_visual == null)
			_visual = transform;
		_visualBasePos = _visual.localPosition;
	}

	public void Appear() => gameObject.SetActive(true);
	public void Collect() => gameObject.SetActive(false);

	public void Tick(float dt)
	{
		_time += dt;
		_visual.localRotation = Quaternion.Euler(0f, _time * _spinSpeed, 0f);
		_visual.localPosition = _visualBasePos + Vector3.up * (Mathf.Sin(_time * _bobSpeed) * _bobAmplitude);
	}

#if UNITY_EDITOR
	private void OnDrawGizmos()
	{
		UnityEditor.Handles.color = new Color(1f, 0.85f, 0.2f, 0.8f);
		UnityEditor.Handles.DrawWireDisc(transform.position, Vector3.up, _pickupRadius);
	}
#endif
}
