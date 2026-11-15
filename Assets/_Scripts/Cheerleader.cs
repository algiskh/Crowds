using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Cheerleader NPC view (prefab built by Tools ▸ Crowds ▸ Build Cheerleader NPC). Not an enemy: no Mob component,
/// no collider in the damage maps → the player's bullets, melee and grenades ignore her. All behaviour lives in
/// ECS.CheerleaderSystem (dance → hide-swap → forgotten/zombie swap on a breather → victory pose → escape on player
/// death). This class only exposes the parts and plays Animator states by name (state names = clip names).
/// See Docs/CheerleaderFeature.md.
/// </summary>
public sealed class Cheerleader : MonoBehaviour
{
	[BoxGroup("Parts"), Required]
	[SerializeField] private Animator _animator;
	[BoxGroup("Parts"), Required, Tooltip("Disabled while on the stage (the deck is Not Walkable); enabled for the escape.")]
	[SerializeField] private NavMeshAgent _agent;
	[BoxGroup("Parts"), Tooltip("Used for the 'is she on screen' test (bounds vs camera frustum).")]
	[SerializeField] private Renderer[] _renderers;
	[BoxGroup("Parts"), Tooltip("Hand props (pompoms) — shown only while dancing.")]
	[SerializeField] private GameObject[] _props;

	[BoxGroup("Stats"), MinValue(1)]
	[SerializeField] private float _maxHealth = 50f;
	[BoxGroup("Stats"), Tooltip("Run speed during the escape, m/s.")]
	[SerializeField] private float _runSpeed = 4.5f;

	[BoxGroup("Animation"), Tooltip("Looping dances, one picked at random; another one after she was off screen.")]
	[SerializeField] private string[] _danceStates;
	[BoxGroup("Animation"), Tooltip("Static poses for the victory — every cheerleader gets a different one.")]
	[SerializeField] private string[] _poseStates;
	[BoxGroup("Animation")]
	[SerializeField] private string _idleState = "Idle";
	[BoxGroup("Animation")]
	[SerializeField] private string _runState = "Goofy Running";
	[BoxGroup("Animation")]
	[SerializeField] private string _deathState = "Falling Forward Death";
	[BoxGroup("Animation"), MinValue(0)]
	[SerializeField] private float _crossFade = 0.25f;

	private string _currentState;

	public NavMeshAgent Agent => _agent;
	public float MaxHealth => _maxHealth;
	public float RunSpeed => _runSpeed;
	public string[] DanceStates => _danceStates;
	public string[] PoseStates => _poseStates;
	public string IdleState => _idleState;
	public string RunState => _runState;
	public string DeathState => _deathState;
	public string CurrentState => _currentState;

	/// <summary>Cross-fades to an Animator state (by name). <paramref name="instant"/> = no blend (she is off screen).</summary>
	public void Play(string state, bool instant = false)
	{
		if (_animator == null || string.IsNullOrEmpty(state))
			return;
		int hash = Animator.StringToHash(state);
		if (!_animator.HasState(0, hash))
		{
			Debug.LogWarning($"[Cheerleader] No Animator state '{state}' on '{name}'.", this);
			return;
		}
		_currentState = state;
		if (instant)
			_animator.Play(hash, 0, Random.value); // random phase so the girls don't move in lockstep
		else
			_animator.CrossFadeInFixedTime(hash, _crossFade);
	}

	/// <summary>Shows/hides the hand props (pompoms): only the dance uses them.</summary>
	public void SetPropsVisible(bool visible)
	{
		if (_props == null)
			return;
		foreach (var prop in _props)
			if (prop != null && prop.activeSelf != visible)
				prop.SetActive(visible);
	}

	/// <summary>Plays a state at a given normalized phase with no blend (keeps the girls' dance in sync).</summary>
	public void PlayAtPhase(string state, float phase)
	{
		if (_animator == null || string.IsNullOrEmpty(state))
			return;
		int hash = Animator.StringToHash(state);
		if (!_animator.HasState(0, hash))
		{
			Debug.LogWarning($"[Cheerleader] No Animator state '{state}' on '{name}'.", this);
			return;
		}
		_currentState = state;
		_animator.Play(hash, 0, phase);
	}

	/// <summary>Freezes/unfreezes the animator and the agent (game pause, fail menu).</summary>
	public void SetFrozen(bool frozen)
	{
		if (_animator != null)
			_animator.speed = frozen ? 0f : 1f;
		if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
			_agent.isStopped = frozen;
	}

	/// <summary>True when any renderer's bounds intersect the camera frustum.</summary>
	public bool IsInView(Plane[] frustum)
	{
		if (_renderers == null)
			return false;
		for (int i = 0; i < _renderers.Length; i++)
		{
			var r = _renderers[i];
			if (r != null && r.enabled && r.gameObject.activeInHierarchy && GeometryUtility.TestPlanesAABB(frustum, r.bounds))
				return true;
		}
		return false;
	}
}
