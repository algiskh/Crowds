using UnityEngine;

public enum RequestSpawnSource
{
    Mob,
    AdditionalSpawn,
    Quest,
    MapLoot,
    // Loot dropped by a destroyed breakable environment object. Like the other
    // non-Mob sources it persists (LootSystem only attaches a despawn timer for Mob).
    Breakable
}
