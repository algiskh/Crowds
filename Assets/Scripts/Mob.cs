using Scene.UI;
using UnityEngine;

public class Mob: MonoBehaviour
{
	[SerializeField] private ValueBar _valueBar;
	[SerializeField] private Collider _collider;
	public Vector2 Position => transform.position;
	public IValueBar ValueBar => _valueBar;
	public Collider Collider => _collider;

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