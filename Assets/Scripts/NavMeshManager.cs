using UnityEngine;
using Unity.AI.Navigation;

public class NavMeshManager : MonoBehaviour
{
	[SerializeField] private NavMeshSurface _navMeshSurface;
	[SerializeField] private FloorSector _currentSector;
	[SerializeField] private FloorSector _rightSector;
	[SerializeField] private FloorSector _leftSector;

	private float _distanceBetweenSectors;
	public FloorSector CurrentSector => _currentSector;
	public FloorSector RightSector => _rightSector;
	public FloorSector LeftSector => _leftSector;
	public float DistanceBetweenSectors => _distanceBetweenSectors;

	private void Awake()
	{
		_distanceBetweenSectors = _currentSector.DistanceTo(_leftSector);
		RebuildNavMesh();
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
