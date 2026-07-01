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
	[Tooltip("Заранее расставленные секторы уровня по порядку (вдоль оси Z).")]
	[SerializeField] private List<FloorSector> _sectors = new();

	private float _distanceBetweenSectors;
	public FloorSector CurrentSector => _currentSector;
	public FloorSector RightSector => _rightSector;
	public FloorSector LeftSector => _leftSector;
	public float DistanceBetweenSectors => _distanceBetweenSectors;

	// Bake'ом и расстоянием между секторами теперь управляет EntryPoint.Configure(...) —
	// чтобы секторы успели приехать из префаба уровня до запекания navmesh. См. план «уровни-префабы».

	// Принимает секторы уровня (из LevelRoot) и запекает navmesh.
	// sectors == null/пусто → используем уже назначенные в инспекторе ссылки (прямой запуск сцены).
	// bake == false → только расставляет секторы/шаг, а запекание оставляет вызывающему
	// (EntryPoint печёт navmesh асинхронно на загрузке — RebuildNavMeshAsync).
	public void Configure(FloorSector[] sectors, bool bake = true)
	{
		if (sectors != null && sectors.Length > 0)
		{
			// Сортируем вдоль Z: для Sliding-режима порядок критичен, для Recycling — даёт тройку.
			var ordered = (FloorSector[])sectors.Clone();
			System.Array.Sort(ordered, (a, b) => a.transform.position.z.CompareTo(b.transform.position.z));

			_sectors = new List<FloorSector>(ordered);

			// Recycling-режим использует тройку current/left/right (префаб содержит ровно 3 сектора).
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

		// Шаг между секторами = расстояние между соседями. Берём из списка (соседние [0],[1]),
		// иначе из назначенной в инспекторе тройки (прямой запуск сцены без префаба).
		if (_sectors != null && _sectors.Count >= 2 && _sectors[0] != null && _sectors[1] != null)
			_distanceBetweenSectors = _sectors[0].DistanceTo(_sectors[1]);
		else if (_currentSector != null && _leftSector != null && _currentSector != _leftSector)
			_distanceBetweenSectors = _currentSector.DistanceTo(_leftSector);

		if (bake)
			RebuildNavMesh();
	}

	// Асинхронное первичное запекание navmesh: не блокирует кадр, поэтому занавес загрузки
	// продолжает анимироваться. Пустой NavMeshData создаётся и добавляется на surface, а затем
	// целиком «обновляется» (UpdateNavMesh перестраивает все изменившиеся регионы — здесь весь меш).
	public AsyncOperation RebuildNavMeshAsync()
	{
		if (_navMeshSurface.navMeshData == null)
		{
			_navMeshSurface.navMeshData = new NavMeshData();
			_navMeshSurface.AddData();
		}

		return _navMeshSurface.UpdateNavMesh(_navMeshSurface.navMeshData);
	}

	// Находит сектор, в границах которого находится точка, иначе ближайший по Z.
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

	// Sliding-режим: включает окно секторов вокруг игрока, остальные выключает.
	// Возвращает центральный (текущий) сектор; перестраивает navmesh только при смене активного набора.
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
