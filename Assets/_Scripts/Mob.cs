using Scene.UI;
using UnityEngine;
using UnityEngine.AI;

public class Mob: MonoBehaviour
{
	[SerializeField] private MeshHealthBar _valueBar;
	[SerializeField] private Collider _collider;
	[SerializeField] private SimpleAnimator _animator;
	public Vector2 Position => transform.position;
	public IValueBar ValueBar => _valueBar;
	public Collider Collider => _collider;
	public SimpleAnimator Animator => _animator;


	private NavMeshAgent _agent;

	private void Awake()
	{
		if (_collider == null)
		{
			_collider = GetComponent<Collider>();
			if (_collider == null)
			{
				Debug.LogError("Collider is not assigned and not found on the Mob GameObject.");
			}
		}
	}

	public string Id { get; private set; }
	public void SetId(string id)
	{
		Id = id;
	}
}