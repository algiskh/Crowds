using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;

public class NavMeshManager : MonoBehaviour
{
	[SerializeField] private NavMeshSurface _navMeshSurface;
	[SerializeField] private FloorSector _currentSector;
	[SerializeField] private FloorSector _rightSector;
	[SerializeField] private FloorSector _leftSector;

	[Header("Sliding mode (SectorMode.Sliding)")]
	[Tooltip("Pre-placed level sectors in order (along the Z axis).")]
	[SerializeField] private List<FloorSector> _sectors = new();

	private float _distanceBetweenSectors;
	public FloorSector CurrentSector => _currentSector;
	public FloorSector RightSector => _rightSector;
	public FloorSector LeftSector => _leftSector;
	public float DistanceBetweenSectors => _distanceBetweenSectors;

	// Baking and the inter-sector distance are now managed by EntryPoint.Configure(...) —
	// so the sectors arrive from the level prefab before the navmesh bake. See the "level-prefabs" plan.

	// Takes the level sectors (from LevelRoot) and bakes the navmesh.
	// sectors == null/empty → use the already assigned inspector references (direct scene launch).
	// bake == false → only arranges sectors/step, leaving the bake to the caller
	// (EntryPoint bakes navmesh asynchronously during loading — RebuildNavMeshAsync).
	public void Configure(FloorSector[] sectors, bool bake = true)
	{
		if (sectors != null && sectors.Length > 0)
		{
			// Sort along Z: for Sliding mode the order is critical, for Recycling it yields the triple.
			var ordered = (FloorSector[])sectors.Clone();
			System.Array.Sort(ordered, (a, b) => a.transform.position.z.CompareTo(b.transform.position.z));

			_sectors = new List<FloorSector>(ordered);

			// Recycling mode uses the current/left/right triple (the prefab contains exactly 3 sectors).
			if (ordered.Length == 3)
			{
				_leftSector = ordered[0];
				_currentSector = ordered[1];
				_rightSector = ordered[2];
			}
			else if (ordered.Length == 1)
			{
				_currentSector = _leftSector = _rightSector = ordered[0];
			}
		}

		// Step between sectors = distance between neighbors. Take it from the list (neighbors [0],[1]),
		// otherwise from the inspector-assigned triple (direct scene launch without a prefab).
		if (_sectors != null && _sectors.Count >= 2 && _sectors[0] != null && _sectors[1] != null)
			_distanceBetweenSectors = _sectors[0].DistanceTo(_sectors[1]);
		else if (_currentSector != null && _leftSector != null && _currentSector != _leftSector)
			_distanceBetweenSectors = _currentSector.DistanceTo(_leftSector);

		// Fallback for finite/one-sector levels (Sliding): without a neighboring sector there's nothing
		// to measure the step from, and the spawn-distance range derived from it collapses to 0 — then the
		// spawn systems (SpawnPointSystem/GroupSpawnSystem) reject ALL points. We take the sector's own
		// size (the larger of the X/Z extents) — this matches the Recycling semantics where
		// the step equals the length of one sector.
		if (_distanceBetweenSectors <= 0f && _currentSector != null)
			_distanceBetweenSectors = GetSectorFootprint(_currentSector);

		if (bake)
			RebuildNavMesh();
	}

	// The larger of the sector's world X/Z extents. Computed from Renderer.bounds (world-space, independent of
	// FloorSector.Awake) — the floor dominates small decor/loot, the union gives its footprint.
	private static float GetSectorFootprint(FloorSector sector)
	{
		var renderers = sector.GetComponentsInChildren<Renderer>();
		if (renderers.Length == 0)
			return 0f;

		var bounds = renderers[0].bounds;
		for (int i = 1; i < renderers.Length; i++)
			bounds.Encapsulate(renderers[i].bounds);

		return Mathf.Max(bounds.size.x, bounds.size.z);
	}

	// Async initial navmesh bake: doesn't block the frame, so the loading curtain
	// keeps animating. An empty NavMeshData is created and added to the surface, then
	// "updated" in full (UpdateNavMesh rebuilds all changed regions — here the whole mesh).
	public AsyncOperation RebuildNavMeshAsync()
	{
		if (_navMeshSurface.navMeshData == null)
		{
			_navMeshSurface.navMeshData = new NavMeshData();
			_navMeshSurface.AddData();
		}

		return _navMeshSurface.UpdateNavMesh(_navMeshSurface.navMeshData);
	}

	// Finds the sector whose bounds contain the point, otherwise the nearest by Z.
	public FloorSector GetNearestSector(Vector3 position)
	{
		FloorSector nearest = null;
		float bestDistance = float.MaxValue;
		foreach (var sector in _sectors)
		{
			if (sector == null)
				continue;
			if (position.IsWithinXZBoundsFromMeshes(sector))
				return sector;
			float distance = Mathf.Abs(position.z - sector.transform.position.z);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				nearest = sector;
			}
		}
		return nearest;
	}

	// Sliding mode: enables the window of sectors around the player, disables the rest.
	// Returns the central (current) sector; rebuilds navmesh only when the active set changes.
	public FloorSector UpdateActiveSectors(Vector3 playerPosition, int activeRadius)
	{
		var center = GetNearestSector(playerPosition);
		if (center == null)
			return null;

		int centerIndex = _sectors.IndexOf(center);
		int min = centerIndex - activeRadius;
		int max = centerIndex + activeRadius;

		bool changed = false;
		for (int i = 0; i < _sectors.Count; i++)
		{
			var sector = _sectors[i];
			if (sector == null)
				continue;
			bool shouldBeActive = i >= min && i <= max;
			if (sector.gameObject.activeSelf != shouldBeActive)
			{
				sector.gameObject.SetActive(shouldBeActive);
				changed = true;
			}
		}

		if (changed)
			RebuildNavMesh();

		return center;
	}

	public void RebuildNavMesh()
	{
		_navMeshSurface.BuildNavMesh();
	}

	public void UpdateSectorsPosition(bool moveForward)
	{
		if (moveForward)
		{
			ShiftSectorPositions(true);
			RotateSectorReferencesCounterClockwise();
		}
		else
		{
			ShiftSectorPositions(false);
			RotateSectorReferencesClockwise();
		}

		RebuildNavMesh();
	}

	public FloorSector GetSector(Vector3 position)
	{
		if (position.IsWithinXZBoundsFromMeshes(_currentSector))
		{
			return _currentSector;
		}
		else if (position.IsWithinXZBoundsFromMeshes(_rightSector))
		{
			return _rightSector;
		}
		else if (position.IsWithinXZBoundsFromMeshes(_leftSector))
		{
			return _leftSector;
		}
		return null;
	}

	private void ShiftSectorPositions(bool shiftRightSector)
	{
		if (shiftRightSector)
		{
			_leftSector.transform.position = _rightSector.transform.position + new Vector3(0, 0, _distanceBetweenSectors);
		}
		else
		{
			_rightSector.transform.position = _leftSector.transform.position - new Vector3(0, 0, _distanceBetweenSectors);
		}
	}

	private void RotateSectorReferencesClockwise()
	{
		(_currentSector, _rightSector, _leftSector) = (_leftSector, _currentSector, _rightSector);
	}

	private void RotateSectorReferencesCounterClockwise()
	{
		(_rightSector, _leftSector, _currentSector) = (_leftSector, _currentSector, _rightSector);
	}
}
