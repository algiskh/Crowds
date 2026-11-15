using UnityEngine;

/// <summary>
/// Scene spawn point of a formation squad. Holds the GroupSpawnConfig; registered in ECS
/// (GroupSpawnPointComponent) by EntryPoint, processed by GroupSpawnSystem.
/// </summary>
public class GroupSpawnPoint : MonoBehaviour
{
	[SerializeField] private GroupSpawnConfig _config;

	public GroupSpawnConfig Config => _config;

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		// Crude visualization of the formation direction (forward) and center.
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(transform.position, 0.5f);
		Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
	}
#endif
}
