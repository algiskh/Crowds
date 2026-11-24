using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A floor area that hands its modifiers to whoever stands in it — the player and every mob (ModifierZoneSystem).
/// The shape comes from the subclass (<see cref="Contains"/>): OilSlick (irregular blob, slow), IceRink (rounded
/// rectangle, inertial sliding via TractionModifier), SewerHatch (circle, light poison, player only). Each modifier is given once per owner and its Lifetime is
/// refreshed to <see cref="Linger"/> every frame while inside, so it never stacks and fades shortly after stepping out.
/// Any modifier type works (speed, traction, damage-over-time…). Active zones live in a static registry
/// (OnEnable/OnDisable). See Docs/HockeyLevelFeature.md.
/// </summary>
public abstract class ModifierZone : MonoBehaviour
{
	private static readonly List<ModifierZone> ActiveList = new();
	public static List<ModifierZone> Active => ActiveList;

	[SerializeReference, Tooltip("Given to the player / mobs standing in the zone. Id identifies the modifier: same " +
	                             "type + Id = refreshed, not stacked.")]
	private Modifier[] _modifiers = System.Array.Empty<Modifier>();
	[SerializeField, Min(0.05f), Tooltip("How long the modifiers linger after stepping out, seconds.")]
	private float _linger = 0.25f;
	[SerializeField, Tooltip("Mobs get the modifiers too. Off = player only (e.g. sewer hatches: toxic zombies are immune).")]
	private bool _affectsMobs = true;
	[SerializeField, Tooltip("On entry, spawn the modifier's own VFX (HasEffect/EffectId), linked to the owner while the " +
	                         "modifier lasts. Off = no VFX (zones re-add their modifiers on every entry).")]
	private bool _entryEffect;

	public Modifier[] Modifiers => _modifiers;
	public float Linger => _linger;
	public bool AffectsMobs => _affectsMobs;
	public bool EntryEffect => _entryEffect;

	/// <summary>True when <paramref name="worldPosition"/> is inside the zone (height ignored).</summary>
	public abstract bool Contains(Vector3 worldPosition);

	protected virtual void OnEnable()
	{
		if (!ActiveList.Contains(this))
			ActiveList.Add(this);
	}

	protected virtual void OnDisable() => ActiveList.Remove(this);

	/// <summary>Editor default (called from a subclass' Reset).</summary>
	protected void SetDefaults(float linger, params Modifier[] modifiers)
	{
		_linger = linger;
		_modifiers = modifiers;
	}

	protected void SetMobs(bool affectsMobs) => _affectsMobs = affectsMobs;
	protected void SetEntryEffect(bool entryEffect) => _entryEffect = entryEffect;
}
