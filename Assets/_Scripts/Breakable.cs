using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A destructible environment prop (crate, barrel, wall). Its footprint carves the baked NavMesh via a
/// carving <see cref="NavMeshObstacle"/> so mobs path around it (no bake/rebuild — see Docs/BreakableFeature.md).
/// Health, damage-source gating, destruction VFX and loot live in <see cref="BreakableConfig"/>; this
/// MonoBehaviour just holds the scene references and applies the visual/navmesh outcome on destruction.
/// The ECS side (BreakableComponent/HealthComponent/ColliderComponent) is created in EntryPoint and driven
/// by BreakableSystem.
/// </summary>
public class Breakable : MonoBehaviour
{
	[SerializeField, Required] private BreakableConfig _config;
	[Tooltip("Solid collider used for bullet/explosion/melee hit detection. Should be on the breakable layer.")]
	[SerializeField] private Collider _collider;
	[Tooltip("Carving obstacle that punches the mob's NavMesh. Carving should be enabled.")]
	[SerializeField] private NavMeshObstacle _obstacle;
	[SerializeField, BoxGroup("Visuals")] private GameObject _intactVisual;
	[SerializeField, BoxGroup("Visuals")] private GameObject _debrisVisual;
	[Tooltip("Points on the mesh where destruction effects spawn (referenced by index from the config).")]
	[SerializeField] private Transform[] _effectPoints;

	public BreakableConfig Config => _config;
	public Collider Collider => _collider;

	private void Awake()
	{
		if (_collider == null)
			_collider = GetComponent<Collider>();
		if (_obstacle == null)
			_obstacle = GetComponent<NavMeshObstacle>();
		// Carving is what makes the object affect the NavMesh without a rebuild.
		if (_obstacle != null)
			_obstacle.carving = true;
		if (_debrisVisual != null)
			_debrisVisual.SetActive(false);
	}

	/// <summary>
	/// Restores the intact state for a fresh spawn or a reuse from the pool: intact visual on, debris off,
	/// collider + carving obstacle on, object active. (Health is reset on the ECS side by BreakableSpawnSystem.)
	/// </summary>
	public void ResetForSpawn()
	{
		if (_intactVisual != null)
			_intactVisual.SetActive(true);
		if (_debrisVisual != null)
			_debrisVisual.SetActive(false);
		if (_collider != null)
			_collider.enabled = true;
		if (_obstacle != null)
		{
			_obstacle.enabled = true;
			_obstacle.carving = true;
		}
		gameObject.SetActive(true);
	}

	/// <summary>World position of the effect point at <paramref name="index"/>; falls back to the object center.</summary>
	public Vector3 GetEffectPoint(int index)
	{
		if (_effectPoints != null && index >= 0 && index < _effectPoints.Length && _effectPoints[index] != null)
			return _effectPoints[index].position;
		return transform.position;
	}

	/// <summary>Debris outcome: swap to the debris visual, stop taking hits, optionally keep carving the NavMesh.</summary>
	public void ShowDebris(bool keepObstacle)
	{
		if (_intactVisual != null)
			_intactVisual.SetActive(false);
		if (_debrisVisual != null)
			_debrisVisual.SetActive(true);
		if (_collider != null)
			_collider.enabled = false;
		if (_obstacle != null)
			_obstacle.enabled = keepObstacle;
	}

	/// <summary>Vanish outcome: remove the obstacle (frees the NavMesh) and deactivate the whole object.</summary>
	public void Vanish()
	{
		if (_obstacle != null)
			_obstacle.enabled = false;
		if (_collider != null)
			_collider.enabled = false;
		gameObject.SetActive(false);
	}
}
