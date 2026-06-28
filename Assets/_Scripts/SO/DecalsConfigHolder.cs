using System;
using UnityEngine;

[Serializable]
public class DecalConfig
{
    public string _id;
    public float _size = 1f;
	[SerializeField] private float _lifetime = 60f;

    [Header("Legacy: one material per variation (random pick)")]
    [SerializeField] private Material[] _materials;

    [Header("Atlas: single material, variation selected by UV cell")]
    [SerializeField] private Material _atlasMaterial;
    [SerializeField, Min(1)] private int _atlasColumns = 1;
    [SerializeField, Min(1)] private int _atlasRows = 1;

    [Header("Per-spawn randomization")]
    [SerializeField] private bool _randomRoll = true;
    [Tooltip("Случайный множитель масштаба (min, max). (1,1) = без джиттера.")]
    [SerializeField] private Vector2 _scaleJitter = Vector2.one;
    [SerializeField] private bool _randomMirror;

    public string Id => _id;
    public float Size => _size;
    public float LifeTime => _lifetime;
    public bool RandomRoll => _randomRoll;

    private bool UseAtlas => _atlasMaterial != null;

	public Material GetMaterial()
    {
        if (UseAtlas)
            return _atlasMaterial;
        return _materials != null && _materials.Length > 0
            ? _materials.GetRandomElement()
            : null;
    }

    /// <summary>
    /// Подбирает случайную ячейку атласа. Возвращает false, если атлас не
    /// используется — тогда UV проектора трогать не нужно.
    /// </summary>
    public bool TryGetRandomCell(out Vector2 uvScale, out Vector2 uvBias)
    {
        if (!UseAtlas)
        {
            uvScale = Vector2.one;
            uvBias = Vector2.zero;
            return false;
        }

        int cols = Mathf.Max(1, _atlasColumns);
        int rows = Mathf.Max(1, _atlasRows);
        int cx = UnityEngine.Random.Range(0, cols);
        int cy = UnityEngine.Random.Range(0, rows);

        float w = 1f / cols;
        float h = 1f / rows;
        float bx = cx * w;
        float by = cy * h;

        if (_randomMirror && UnityEngine.Random.value < 0.5f)
        {
            w = -w;       // отражаем ячейку по горизонтали
            bx += 1f / cols;
        }

        uvScale = new Vector2(w, h);
        uvBias = new Vector2(bx, by);
        return true;
    }

    /// <summary>Случайный множитель масштаба из диапазона _scaleJitter.</summary>
    public float GetScaleJitter()
    {
        if (_scaleJitter.x <= 0f && _scaleJitter.y <= 0f)
            return 1f;
        return UnityEngine.Random.Range(_scaleJitter.x, _scaleJitter.y);
    }
}

[CreateAssetMenu(fileName = "DecalsConfigHolder", menuName = "Scriptable Objects/DecalsConfigHolder")]
public class DecalsConfigHolder : ScriptableObject
{
    [SerializeField] private DecalConfig[] _configs;
    [SerializeField] private Decal _prefab;

    public Decal Prefab => _prefab;

    public DecalConfig GetConfig(string id)
    {
        foreach (var config in _configs)
        {
            if (config.Id.Equals(id))
            {
                return config;
            }
        }
        return null;
    }
}
