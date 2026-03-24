using UnityEngine;

[CreateAssetMenu(fileName = "MeleeConfig", menuName = "Scriptable Objects/MeleeConfig")]
public class MeleeConfig : ScriptableObject
{
    [SerializeField] private float _damage;
    [SerializeField] private float _range;
    [SerializeField] private float _radius;
    [SerializeField] private Modifier[] _modifiers;
    [SerializeField] private TargetType _targetType;

	[SerializeField] private float _delay;
	[SerializeField] private float _cooldown;

	public float Damage => _damage;
	public float Range => _range;
	public float Radius => _radius;
	public Modifier[] Modifiers => _modifiers;
	public TargetType TargetType => _targetType;

	public float Delay => _delay;
	public float Cooldown => _cooldown;
}
