using System;

[Serializable]
public class PossibleLoot
{
	public LootType LootType;
	public string Id; // Need for weapons
	public int Count;
	public float Chance;
}