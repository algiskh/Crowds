using System;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Конфиг отряда (группы мобов в строю): тип строя, ведущий, список ведомых, межслотовые
/// отступы и пресеты спауна по уровням сложности. Как и обычная точка (MobSpawnConfig),
/// отряд спаунится только на тех DifficultyLevel, для которых задан пресет — так группы
/// «привязаны к уровню/стадии». Ведомые в v1 — обычные мобы (без melee/grenadier-поведения),
/// чтобы строй и боевые состояния не конфликтовали.
/// </summary>
[CreateAssetMenu(fileName = "GroupSpawnConfig", menuName = "Scriptable Objects/GroupSpawnConfig")]
public class GroupSpawnConfig : ScriptableObject
{
	[Title("Строй")]
	[SerializeField] private ECS.FormationType _formation = ECS.FormationType.Wedge;
	[SerializeField, Required] private string _leaderMobId;
	[SerializeField] private FollowerEntry[] _followers;

	[Title("Геометрия")]
	[SerializeField, MinValue(0.1f)] private float _spacingX = 1.6f;
	[SerializeField, MinValue(0.1f)] private float _spacingZ = 1.6f;

	[Title("Спаун по уровням сложности")]
	[Tooltip("На каких стадиях сложности появляется отряд и с каким базовым кулдауном. " +
		"Нет пресета для текущей стадии — отряд не спаунится.")]
	[SerializeField] private SpawnPreset[] _spawnPresets;
	[SerializeField, MinValue(0f), Tooltip("Задержка перед первой проверкой спауна точки.")]
	private float _initialDelay = 0f;

	public ECS.FormationType Formation => _formation;
	public string LeaderMobId => _leaderMobId;
	public FollowerEntry[] Followers => _followers;
	public float SpacingX => _spacingX;
	public float SpacingZ => _spacingZ;
	public float InitialDelay => _initialDelay;

	/// <summary>
	/// Базовый кулдаун для стадии сложности. false — пресета нет, отряд на этой стадии не спаунится.
	/// </summary>
	public bool TryGetCooldown(DifficultyLevel level, out float cooldown)
	{
		if (_spawnPresets != null)
		{
			foreach (var preset in _spawnPresets)
			{
				if (preset.DifficultyLevel == level)
				{
					cooldown = preset.Cooldown;
					return true;
				}
			}
		}
		cooldown = 0f;
		return false;
	}

	[Serializable]
	public class FollowerEntry
	{
		[SerializeField] private string _mobId;
		[SerializeField, MinValue(1)] private int _count = 1;

		public string MobId => _mobId;
		public int Count => _count;
	}
}
