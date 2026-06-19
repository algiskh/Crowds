using Sirenix.OdinInspector;
using UnityEngine;
#if UNITY_EDITOR
using System.Linq;
#endif

// Корень контента уровня — кладётся на КОРЕНЬ префаба уровня.
// Универсальная геймплейная сцена инстанциирует префаб и читает отсюда конфиг и секторы.
// Спавн-поинты (SpawnPoint), MapLoot и объекты с тегом "AdditionalSpawn" обнаруживаются
// EntryPoint'ом через Find*/тег, поэтому их достаточно просто положить внутрь префаба.
// См. Docs/SectorFeature.md и план «уровни-префабы».
public class LevelRoot : MonoBehaviour
{
	[SerializeField, Required, BoxGroup("Level")]
	private LevelConfig _levelConfig;

	[SerializeField, BoxGroup("Level"),
	 Tooltip("Секторы пола по порядку вдоль оси Z. Для Sliding-режима порядок важен. " +
	         "Кнопка ниже соберёт их автоматически из дочерних объектов.")]
	private FloorSector[] _sectors;

	public LevelConfig LevelConfig => _levelConfig;
	public FloorSector[] Sectors => _sectors;

#if UNITY_EDITOR
	[Button("Collect sectors (sorted by Z)"), BoxGroup("Level")]
	private void CollectSectors()
	{
		_sectors = GetComponentsInChildren<FloorSector>(true)
			.OrderBy(s => s.transform.position.z)
			.ToArray();
		UnityEditor.EditorUtility.SetDirty(this);
	}
#endif
}
