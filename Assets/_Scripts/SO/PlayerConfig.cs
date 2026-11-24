using UnityEngine;

//[CreateAssetMenu(fileName = "PlayerConfig", menuName = "Scriptable Objects/PlayerConfig", order = 1)]
public class PlayerConfig : ScriptableObject
{
	[Header("Player Basic Settings")]
	[SerializeField] private float _basicSpeed = 5f;
    [SerializeField] private float _basicMaxHealth = 100f;

	[Tooltip("After taking a hit the player ignores further hits for this long (sec), so a crowd " +
	         "can't stack contact damage into an instant death. DoT ticks (poison) ignore it. 0 = off.")]
	[SerializeField, Min(0f)] private float _hitInvulnerabilityTime = 0.4f;

	[Tooltip("Off (default): walking over a weapon swaps the gun — losing a good gun by accident is part of the fun. " +
	         "On: a different weapon is picked up only by holding Interact (reserved for a future perk).")]
	[SerializeField] private bool _weaponPickupRequiresInteract = false;

	public float Speed => _basicSpeed;
	public bool WeaponPickupRequiresInteract => _weaponPickupRequiresInteract;
	public float MaxHealth => _basicMaxHealth;
	public float HitInvulnerabilityTime => _hitInvulnerabilityTime;

	[Header("Ice")]
	[Tooltip("Player inertia on slippery ground (TractionModifier) relative to the zone's grip: 1.2 = 20% more " +
	         "inertia (grip / 1.2) — speeds up, turns and stops more slowly than the mobs on the same ice.")]
	[SerializeField, Min(0.1f)] private float _iceInertia = 1.2f;

	public float IceInertia => _iceInertia;

	// Per-grenade stats (speed, damage, radius, effects) live in GrenadeConfig.
	// Here only player-level throw mechanics: starting count, distance range, charge time.
	[Header("Grenade - inventory")]
	[SerializeField] private int _startGrenades = 0;
	[Tooltip("Grenade-config id for the starting grenades (from GrenadeConfigHolder). Empty = first in the holder.")]
	[SerializeField] private string _startGrenadeId = "";

	[Header("Grenade - throw")]
	[Tooltip("Minimum throw distance (on instant release).")]
	[SerializeField] private float _minThrowDistance = 3f;
	[Tooltip("Maximum throw distance (at full charge).")]
	[SerializeField] private float _maxThrowDistance = 10f;
	[Tooltip("How long Throw is held (sec) to reach full distance.")]
	[SerializeField] private float _maxThrowChargeTime = 1.2f;

	public int StartGrenades => _startGrenades;
	public string StartGrenadeId => _startGrenadeId;
	public float MinThrowDistance => _minThrowDistance;
	public float MaxThrowDistance => _maxThrowDistance;
	public float MaxThrowChargeTime => _maxThrowChargeTime;

	[Header("Damage feedback (vignette / shake / hit marker)")]
	[SerializeField] private DamageFeedbackSettings _damageFeedback = new();

	public DamageFeedbackSettings DamageFeedback => _damageFeedback;

	[Header("See-through (round hole in occluders between the camera and the player)")]
	[SerializeField] private SeeThroughSettings _seeThrough = new();

	public SeeThroughSettings SeeThrough => _seeThrough;
}

/// <summary>
/// Soft round "window" in the world geometry that covers the player (Crowds/Lit See-Through shader, driven by
/// ECS.SeeThroughSystem): only what is closer to the camera than the player AND above the player's feet is cut,
/// so the floor stays intact. Materials on the Lit See-Through shader only (environment, not characters).
/// </summary>
[System.Serializable]
public class SeeThroughSettings
{
	[Tooltip("Turn the effect off entirely (the shader then behaves as plain Lit).")]
	public bool Enabled = true;

	[Tooltip("Hole radius as a share of the screen height.")]
	[Range(0.02f, 0.5f)] public float Radius = 0.11f;

	[Tooltip("Soft dithered rim as a share of the radius (0 = hard edge, 1 = fades from the centre).")]
	[Range(0.01f, 1f)] public float EdgeSoftness = 0.45f;

	[Tooltip("Point the hole is centred on, above the player's pivot (chest height).")]
	public float FocusHeight = 1.0f;

	[Tooltip("Geometry must be at least this much closer to the camera than the player to be cut (world units).")]
	[Min(0f)] public float DepthMargin = 0.4f;

	[Tooltip("Depth range over which the cut fades in after the margin (world units).")]
	[Min(0.01f)] public float DepthFade = 0.8f;

	[Tooltip("Cut only above the player's feet + this offset, so the floor in front of the player survives.")]
	public float HeightOffset = 0.2f;

	[Tooltip("Height range over which the cut fades in above the offset (world units).")]
	[Min(0.01f)] public float HeightFade = 0.35f;

	[Tooltip("How fast the hole opens/closes when it is toggled (strength per second).")]
	[Min(0.1f)] public float FadeSpeed = 4f;
}

/// <summary>
/// How the player's injuries are shown on screen (DamageFeedbackSystem + DamageFeedbackOverlay):
/// a red edge vignette that flashes in proportion to the hit, a steady pulse at low health, a short camera
/// shake and an edge arc pointing toward where the hit came from.
/// </summary>
[System.Serializable]
public class DamageFeedbackSettings
{
	[Tooltip("Vignette colour (alpha is ignored — the intensity drives it).")]
	public Color VignetteColor = new(0.75f, 0f, 0f, 1f);

	[Tooltip("Vignette alpha of a 'full' hit. Smaller hits flash proportionally weaker.")]
	[Range(0f, 1f)] public float MaxFlashAlpha = 0.65f;

	[Tooltip("Damage as a share of max health that gives the full flash (0.25 = a hit of 25% HP).")]
	[Min(0.01f)] public float FullFlashDamageShare = 0.25f;

	[Tooltip("Minimum flash of any direct hit, so tiny hits are still noticed.")]
	[Range(0f, 1f)] public float MinFlashAlpha = 0.2f;

	[Tooltip("Flash strength multiplier for DoT ticks (poison). 0 = ticks don't flash.")]
	[Range(0f, 1f)] public float PeriodicFlashScale = 0.35f;

	[Tooltip("Seconds for a flash to fade out.")]
	[Min(0.01f)] public float FlashFadeTime = 0.45f;

	[Tooltip("Health share below which the vignette pulses permanently. 0 = off.")]
	[Range(0f, 1f)] public float LowHealthThreshold = 0.25f;

	[Tooltip("Pulse alpha at (near) zero health; scales down toward the threshold.")]
	[Range(0f, 1f)] public float LowHealthPulseAlpha = 0.4f;

	[Tooltip("Pulses per second at low health.")]
	[Min(0.01f)] public float LowHealthPulseRate = 1.4f;

	[Tooltip("Camera shake length on a direct hit (sec). 0 = no shake.")]
	[Min(0f)] public float ShakeDuration = 0.12f;

	[Tooltip("Camera shake amplitude (world units) of a full hit; smaller hits shake proportionally.")]
	[Min(0f)] public float ShakeAmplitude = 0.18f;

	[Tooltip("Show an arc at the screen edge pointing toward the hit source.")]
	public bool ShowHitDirection = true;

	[Tooltip("Seconds the direction arc stays visible (fades out).")]
	[Min(0.05f)] public float HitDirectionTime = 0.8f;
}
