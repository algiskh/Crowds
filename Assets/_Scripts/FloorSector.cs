using UnityEngine;

public class FloorSector : MonoBehaviour
{
	private MeshFilter[] _meshFilters;

	public MeshFilter[] MeshFilters => _meshFilters;

	private void Awake()
	{
		_meshFilters = GetComponentsInChildren<MeshFilter>();
	}
}
