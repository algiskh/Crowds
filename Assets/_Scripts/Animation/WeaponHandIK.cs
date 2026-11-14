using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Puts the left hand on the held weapon's forend (<see cref="WeaponModel.LeftHandTarget"/>) with a two-bone IK
/// (upper arm → forearm → hand) applied in LateUpdate on top of the animation. Works with Generic rigs (the Gunner
/// is Generic, so Animator.OnAnimatorIK is unavailable). The animated elbow position is kept as the bend hint, so the
/// arm keeps the clip's bend plane. Bones are found by name under the Animator, the weapon is read from
/// <see cref="Mob.Weapon"/> every frame (survives Mob.ReplaceWeapon) or from <see cref="_weapon"/>.
///
/// Why: Mixamo "Rifle Walk"/"Rifle Idle" carry the gun lower than "Rifle Aiming Idle", while the weapon is
/// aligned to the aim pose in the right hand — the left hand used to hang in the air during the chase.
/// </summary>
public class WeaponHandIK : MonoBehaviour
{
	[BoxGroup("Refs"), Tooltip("Source of the held weapon (Mob.Weapon). Empty = use the explicit weapon below.")]
	[SerializeField] private Mob _mob;
	[BoxGroup("Refs"), Tooltip("Explicit weapon (when there is no Mob, e.g. the player).")]
	[SerializeField] private WeaponModel _weapon;
	[BoxGroup("Refs"), Tooltip("Animator of the body. Empty = first Animator in children.")]
	[SerializeField] private Animator _animator;

	[BoxGroup("Bones"), Tooltip("Bone names (searched under the Animator).")]
	[SerializeField] private string _upperArmBone = "mixamorig:LeftArm";
	[BoxGroup("Bones")]
	[SerializeField] private string _forearmBone = "mixamorig:LeftForeArm";
	[BoxGroup("Bones")]
	[SerializeField] private string _handBone = "mixamorig:LeftHand";

	[BoxGroup("Weight"), Range(0f, 1f), Tooltip("Position weight while active.")]
	[SerializeField] private float _weight = 1f;
	[BoxGroup("Weight"), Range(0f, 1f),
	 Tooltip("How much the hand also takes the target's rotation (0 = keep the animated wrist rotation).")]
	[SerializeField] private float _rotationWeight;
	[BoxGroup("Weight"), Tooltip("Animator states (base layer) where the IK is OFF, e.g. aim/shoot if the clip already " +
	                             "holds the forend, or die (the body falls, the hand must let go).")]
	[SerializeField] private string[] _disabledStates = { "die" };
	[BoxGroup("Weight"), Tooltip("Animator layer whose current state is checked against the disabled states " +
	                             "(empty = base layer). Player: \"UpperBody\" — Reload/Melee/Throw live there.")]
	[SerializeField] private string _stateLayer;
	[BoxGroup("Weight"), Min(0.01f), Tooltip("Weight change per second when entering/leaving a disabled state.")]
	[SerializeField] private float _blendSpeed = 6f;

	private Transform _upper, _lower, _hand;
	private readonly HashSet<int> _disabledHashes = new();
	private float _currentWeight;
	private int _layerIndex;
	private bool _resolved;

	private void Awake()
	{
		if (_mob == null)
			_mob = GetComponent<Mob>();
		if (_animator == null)
			_animator = GetComponentInChildren<Animator>(true);
		foreach (var state in _disabledStates)
			if (!string.IsNullOrEmpty(state))
				_disabledHashes.Add(Animator.StringToHash(state));
		_layerIndex = 0;
		if (_animator != null && !string.IsNullOrEmpty(_stateLayer))
		{
			_layerIndex = _animator.GetLayerIndex(_stateLayer);
			if (_layerIndex < 0)
			{
				Debug.LogWarning($"[WeaponHandIK] {name}: layer '{_stateLayer}' not found, using the base layer.", this);
				_layerIndex = 0;
			}
		}
		Resolve();
	}

	/// <summary>Explicit weapon (player: PlayerAnimator passes the current gun model). Ignored while a Mob has one.</summary>
	public void SetWeapon(WeaponModel weapon) => _weapon = weapon;

	private void OnEnable()
	{
		// Pooled mobs are re-enabled for a new life: start blended in, not from the dead pose.
		_currentWeight = _weight;
	}

	private void Resolve()
	{
		var root = _animator != null ? _animator.transform : transform;
		_upper = FindDeep(root, _upperArmBone);
		_lower = FindDeep(root, _forearmBone);
		_hand = FindDeep(root, _handBone);
		_resolved = _upper != null && _lower != null && _hand != null;
		if (!_resolved)
			Debug.LogWarning($"[WeaponHandIK] {name}: left arm bones not found " +
			                 $"({_upperArmBone}/{_forearmBone}/{_handBone}), IK disabled.", this);
	}

	private static Transform FindDeep(Transform root, string boneName)
	{
		if (string.IsNullOrEmpty(boneName))
			return null;
		if (root.name == boneName)
			return root;
		for (int i = 0; i < root.childCount; i++)
		{
			var found = FindDeep(root.GetChild(i), boneName);
			if (found != null)
				return found;
		}
		return null;
	}

	private bool IsInDisabledState()
	{
		if (_animator == null || _disabledHashes.Count == 0 || !_animator.isActiveAndEnabled)
			return false;
		// The base layer is always checked too (e.g. the player's Death lives there, Reload in UpperBody).
		return IsLayerInDisabledState(_layerIndex) || (_layerIndex != 0 && IsLayerInDisabledState(0));
	}

	private bool IsLayerInDisabledState(int layer)
	{
		// During a cross-fade judge by the state we are going to.
		var info = _animator.IsInTransition(layer)
			? _animator.GetNextAnimatorStateInfo(layer)
			: _animator.GetCurrentAnimatorStateInfo(layer);
		return _disabledHashes.Contains(info.shortNameHash);
	}

	private void LateUpdate()
	{
		if (!_resolved)
			return;
		var weapon = _mob != null && _mob.Weapon != null ? _mob.Weapon : _weapon;
		var target = weapon != null && weapon.isActiveAndEnabled ? weapon.LeftHandTarget : null;

		float wanted = target != null && !IsInDisabledState() ? _weight : 0f;
		// Animator speed 0 = game pause: freeze the blend too.
		float dt = _animator != null && _animator.speed <= 0f ? 0f : Time.deltaTime;
		_currentWeight = Mathf.MoveTowards(_currentWeight, wanted, _blendSpeed * dt);
		if (target == null || _currentWeight <= 0.001f)
			return;

		Solve(_upper, _lower, _hand, target.position, _currentWeight);
		if (_rotationWeight > 0f)
			_hand.rotation = Quaternion.Slerp(_hand.rotation, target.rotation, _rotationWeight * _currentWeight);
	}

	/// <summary>Analytic two-bone IK (same math as Animation Rigging's TwoBoneIK), bend plane from the current pose.</summary>
	private static void Solve(Transform root, Transform mid, Transform tip, Vector3 targetPos, float weight)
	{
		Vector3 a = root.position;
		Vector3 b = mid.position;
		Vector3 c = tip.position;
		Vector3 t = Vector3.Lerp(c, targetPos, weight);

		Vector3 ab = b - a;
		Vector3 bc = c - b;
		Vector3 ac = c - a;
		Vector3 at = t - a;

		float abLen = ab.magnitude;
		float bcLen = bc.magnitude;
		if (abLen < 1e-5f || bcLen < 1e-5f)
			return;
		float acLen = ac.magnitude;
		float atLen = Mathf.Clamp(at.magnitude, Mathf.Abs(abLen - bcLen) + 1e-4f, abLen + bcLen - 1e-4f);

		// 1. Elbow: change the angle at B so that |A→C| becomes |A→T|.
		float oldAngle = TriangleAngle(acLen, abLen, bcLen);
		float newAngle = TriangleAngle(atLen, abLen, bcLen);
		Vector3 axis = Vector3.Cross(ab, bc);
		if (axis.sqrMagnitude < 1e-8f)
		{
			axis = Vector3.Cross(at, bc);
			if (axis.sqrMagnitude < 1e-8f)
				axis = Vector3.up;
		}
		axis.Normalize();
		float half = 0.5f * (oldAngle - newAngle);
		float sin = Mathf.Sin(half);
		var delta = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, Mathf.Cos(half));
		mid.rotation = delta * mid.rotation;

		// 2. Shoulder: swing the whole chain so the hand points at the target.
		ac = tip.position - a;
		if (ac.sqrMagnitude > 1e-8f && at.sqrMagnitude > 1e-8f)
			root.rotation = Quaternion.FromToRotation(ac, at) * root.rotation;
	}

	private static float TriangleAngle(float opposite, float side1, float side2)
	{
		float cos = (side1 * side1 + side2 * side2 - opposite * opposite) / (2f * side1 * side2);
		return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f));
	}
}
