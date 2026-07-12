using Sirenix.OdinInspector;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a breakable does with itself once it is destroyed.
/// Vanish — the whole object is deactivated (its NavMesh-carving obstacle is removed, freeing the area).
/// Debris — the object swaps to a debris visual; it may keep carving the NavMesh (see DebrisKeepsObstacle).
/// </summary>
public enum AfterDestruction
{
	Vanish,
	Debris
}

/// <summary>
/// One destruction VFX: an effect (by id from <see cref="EffectsHolder"/>) spawned at a mesh point
/// after an optional delay. Several of these let a prop erupt in staggered bursts
/// (e.g. two explosions, the second starting later).
/// </summary>
[Serializable]
public class DestructionEffect
{
	[ValueDropdown(nameof(GetEffectIds))] public string EffectId;
	[Tooltip("Seconds to wait after destruction before this effect plays.")]
	public float Delay;
	[Tooltip("Index into the Breakable's Effect Points. -1 (or out of range) = object center.")]
	public int PointIndex = -1;

	private IEnumerable<string> GetEffectIds()
	{
		var holder = EffectsHolder.Instance;
		if (holder == null)
			yield break;
		foreach (var fx in holder.GetAll())
			yield return fx.Id;
	}
}

[CreateAssetMenu(fileName = "BreakableConfig", menuName = "Scriptable Objects/BreakableConfig")]
public class BreakableConfig : ScriptableObject
{
	[Tooltip("Unique id for holder lookup and pooling. Must match across the holder and any spawn request.")]
	[SerializeField, BoxGroup("Identity")] private string _id;
	[Tooltip("Prefab spawned for this breakable (both scene-placed and runtime RequestSpawnBreakable).")]
	[SerializeField, Required, BoxGroup("Identity")] private Breakable _prefab;

	[SerializeField, BoxGroup("Health")] private float _maxHealth = 100f;

	[Tooltip("Which damage sources are allowed to destroy this object.")]
	[SerializeField, BoxGroup("Health")] private BreakableDamageSources _damageableBy =
		BreakableDamageSources.Bullet | BreakableDamageSources.Explosion;

	[Tooltip("Effects spawned on destruction, each at a mesh point with its own delay.")]
	[SerializeField, BoxGroup("Destruction")] private DestructionEffect[] _destructionEffects;

	[Title("Destruction blast")]
	[Tooltip("Deal radial damage on destruction (exploding barrels, etc.). Reuses the explosion system: " +
	         "center→edge falloff, hits mobs and the player, and can chain-detonate nearby breakables.")]
	[SerializeField, BoxGroup("Blast")] private bool _damageOnDestruction = false;
	[Tooltip("Blast radius. No damage outside it.")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), MinValue(0f)] private float _damageRadius = 3f;
	[Tooltip("Damage at the center of the blast (lerps down to Min Damage at the edge).")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), MinValue(0f)] private float _maxDamage = 40f;
	[Tooltip("Damage at the edge of the blast.")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), MinValue(0f)] private float _minDamage = 10f;
	[Tooltip("Fraction of the blast dealt to mobs (0..1). 0 = mobs are not hurt.")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), Range(0f, 1f)] private float _mobDamageScale = 1f;
	[Tooltip("Fraction of the blast dealt to the player (0..1). 0 = the player is not hurt.")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), Range(0f, 1f)] private float _playerDamageScale = 1f;
	[Tooltip("Explosion VFX id for the blast. Empty = the default \"explosion\" effect (staggered destruction effects still play).")]
	[SerializeField, BoxGroup("Blast"), ShowIf(nameof(_damageOnDestruction)), ValueDropdown(nameof(GetEffectIds))] private string _damageEffectId;

	[Title("Loot")]
	[Tooltip("Drop table rolled once per spawned loot item (same rules as mob drops).")]
	[SerializeField, BoxGroup("Loot")] private PossibleLoot[] _lootTable;
	[Tooltip("How many loot items to spawn on destruction (each rolls the table independently).")]
	[SerializeField, BoxGroup("Loot"), MinValue(0)] private int _lootCount = 0;
	[Tooltip("Radius of the ring the loot items are scattered on (min spacing between them).")]
	[SerializeField, BoxGroup("Loot"), MinValue(0f)] private float _lootSpread = 1f;

	[Title("After destruction")]
	[SerializeField, BoxGroup("After")] private AfterDestruction _afterDestruction = AfterDestruction.Vanish;
	[Tooltip("Debris mode only: keep carving the NavMesh (debris still blocks) or free it.")]
	[SerializeField, BoxGroup("After")] private bool _debrisKeepsObstacle = true;

	public string Id => _id;
	public Breakable Prefab => _prefab;
	public float MaxHealth => _maxHealth;
	public DestructionEffect[] DestructionEffects => _destructionEffects;
	public bool DamageOnDestruction => _damageOnDestruction;
	public float DamageRadius => _damageRadius;
	public float MaxDamage => _maxDamage;
	public float MinDamage => _minDamage;
	public float MobDamageScale => _mobDamageScale;
	public float PlayerDamageScale => _playerDamageScale;
	public string DamageEffectId => _damageEffectId;
	public PossibleLoot[] LootTable => _lootTable;
	public int LootCount => _lootCount;
	public float LootSpread => _lootSpread;
	public AfterDestruction AfterDestruction => _afterDestruction;
	public bool DebrisKeepsObstacle => _debrisKeepsObstacle;

	/// <summary>True if any of the given source flags is allowed to damage this object.</summary>
	public bool CanBeDamagedBy(BreakableDamageSources source) => (_damageableBy & source) != 0;

	// Effect id dropdown for the blast field (mirrors DestructionEffect.GetEffectIds).
	private IEnumerable<string> GetEffectIds()
	{
		var holder = EffectsHolder.Instance;
		if (holder == null)
			yield break;
		foreach (var fx in holder.GetAll())
			yield return fx.Id;
	}
}
