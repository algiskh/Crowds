using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public class FloorSector : MonoBehaviour
{
	private MeshFilter[] _meshFilters;

	public MeshFilter[] MeshFilters => _meshFilters;

	private void Awake()
	{
		_meshFilters = GetComponentsInChildren<MeshFilter>();
	}
}
