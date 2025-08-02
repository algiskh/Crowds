
using System;
using UnityEngine;

[CreateAssetMenu(fileName = "MobConfig", menuName = "Scriptable Objects/MobConfig", order = 1)]
public class MobConfig: ScriptableObject
{
	[Serializable]
	public class PossibleLoot
	{
		public LootType LootType;
		public string Id; // Need for weapons
		public int Count;
		public float Chance;
	}

	[SerializeField] private string _id;
	[SerializeField] private float _health;
	[SerializeField] private float _speed;
	[SerializeField] private float _collisionRadius;
	[SerializeField] private Mob _prefab;
	[SerializeField] private PossibleLoot[] _possibleLoots;

	public string Id => _id;
	public float Health => _health;
	public float Speed => _speed;
	public Mob Prefab => _prefab;
	public float CollisionRadius => _collisionRadius;
	public PossibleLoot[] PossibleLoots => _possibleLoots;
}
