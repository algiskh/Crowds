using UnityEngine;

public class Loot : MonoBehaviour
{
	private LootType _lootType;

	public void SetLootType(LootType type)
	{
		_lootType = type;
	}
}
