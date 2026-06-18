using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Конфиг моба ближнего боя. Во всех «обычных» параметрах (здоровье, скорость, лут,
/// модификаторы) идентичен MobConfig — наследуется от него. Добавляет только дистанцию
/// начала атаки и ссылку на MeleeConfig — ту же «категорию» ближней атаки, что использует
/// игрок (урон, радиус, цель, замах Delay и кулдаун Cooldown задаются там).
/// Поведение — см. MeleeAttackerSystem.
/// </summary>
[CreateAssetMenu(fileName = "MeleeMobConfig", menuName = "Scriptable Objects/MeleeMobConfig", order = 3)]
public class MeleeMobConfig : MobConfig
{
	[Title("Melee Attacker")]
	[Tooltip("Дистанция, с которой моб останавливается и начинает атаку. Должна быть >= Range у MeleeConfig.")]
	[SerializeField, MinValue(0f)] private float _attackRange = 2f;

	[Required, Tooltip("Категория ближней атаки: урон, радиус, цель, модификаторы, замах (Delay) и кулдаун (Cooldown). Та же модель, что у атаки игрока.")]
	[SerializeField] private MeleeConfig _meleeConfig;

	public float AttackRange => _attackRange;
	public MeleeConfig MeleeConfig => _meleeConfig;
}
