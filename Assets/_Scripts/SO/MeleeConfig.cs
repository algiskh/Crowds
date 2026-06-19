using Sirenix.Serialization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;



[CreateAssetMenu(fileName = "MeleeConfig", menuName = "Scriptable Objects/MeleeConfig")]
public class MeleeConfig : ScriptableObject
{
	[SerializeField] private string _id;
	[SerializeField] private float _damage;
    [SerializeField] private float _range;
    [SerializeField] private float _radius;
    [SerializeReference, OdinSerialize] private Modifier[] _modifiers;
    [SerializeField] private TargetType _targetType;

	[Tooltip("Wind-up: pause before the hit lands (sec). For mobs - the 'pre-attack' animation phase.")]
	[SerializeField] private float _delay;
	[Tooltip("Recovery after the hit (sec). For mobs - the cooldown phase of the same animation; for the player - the delay between hits.")]
	[SerializeField] private float _cooldown;

	[SerializeReference, OdinSerialize] private Modifier[] _debuffs;

	public string Id => _id;
	public float Damage => _damage;
	public float Range => _range;
	public float Radius => _radius;
	public Modifier[] Modifiers => _modifiers;
	public Modifier[] Debuffs => _debuffs;
	public TargetType TargetType => _targetType;

	public float Delay => _delay;
	public float Cooldown => _cooldown;

	public IEnumerable<Modifier> GetAllModifiersAsCopies(bool isDebuffs = false)
	{
		var modifiers = isDebuffs ? _debuffs : _modifiers;
		if (modifiers == null)
		{
			yield break;
		}

		foreach (var modifier in modifiers)
		{
			if (modifier != null)
			{
				yield return modifier.Clone<Modifier>();
			}
		}
	}
}
