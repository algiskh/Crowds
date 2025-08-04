using UnityEngine;
using UnityEngine.AI;

public class AgentDebugger : MonoBehaviour
{
	private NavMeshAgent _agent;

	private void Start()
	{
		_agent = GetComponent<NavMeshAgent>();
		if (_agent == null) Debug.LogError("Нет NavMeshAgent на объекте!");
	}

	private void Update()
	{
		if (_agent.pathPending)
		{
			Debug.DrawLine(_agent.transform.position, _agent.destination, Color.yellow);
			return;
		}

		if (_agent.hasPath)
		{
			var corners = _agent.path.corners;
			for (int i = 0; i < corners.Length - 1; i++)
				Debug.DrawLine(corners[i], corners[i + 1], Color.green);

			if (_agent.isOnOffMeshLink)
			{
				Debug.Log("🔥 АГЕНТ НАХОДИТСЯ НА OFFMESH LINK");
				var data = _agent.currentOffMeshLinkData;
				Debug.DrawLine(data.startPos, data.endPos, Color.red, 1f);

				// Для теста — перейти сразу
				_agent.Warp(data.endPos);
				_agent.CompleteOffMeshLink();
			}
		}
		else
		{
			Debug.Log("⚠️ Агент не имеет пути.");
		}
	}
}