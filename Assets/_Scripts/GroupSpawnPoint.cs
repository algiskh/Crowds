using UnityEngine;

/// <summary>
/// Сценовая точка спауна отряда в строю. Держит GroupSpawnConfig; регистрируется в ECS
/// (GroupSpawnPointComponent) в EntryPoint, обрабатывается GroupSpawnSystem.
/// </summary>
public class GroupSpawnPoint : MonoBehaviour
{
	[SerializeField] private GroupSpawnConfig _config;

	public GroupSpawnConfig Config => _config;

#if UNITY_EDITOR
	private void OnDrawGizmosSelected()
	{
		// Грубая визуализация направления строя (вперёд) и центра.
		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(transform.position, 0.5f);
		Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
	}
#endif
}
