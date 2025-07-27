using UnityEngine;

//[CreateAssetMenu(fileName = "PlayerConfig", menuName = "Scriptable Objects/PlayerConfig", order = 1)]
public class PlayerConfig : ScriptableObject
{
	[Header("Player Basic Settings")]
	[SerializeField] private float _basicSpeed = 5f;
    [SerializeField] private float _basicMaxHealth = 100f;

	public float Speed => _basicSpeed;
	public float MaxHealth => _basicMaxHealth;

}
