using UnityEngine;

public class MapLoot : MonoBehaviour
{
	[SerializeField] private Loot _loot;

	[SerializeField] private LootComponent _lootComponent;
	public LootComponent LootComponent => _lootComponent;
}
