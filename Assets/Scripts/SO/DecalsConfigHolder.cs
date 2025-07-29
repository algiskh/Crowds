using System;
using UnityEngine;

[Serializable]
public class DecalConfig
{
    public string _id;
    public float _size = 1f;
	[SerializeField] private float _lifetime = 60f;
    [SerializeField] private Material[] _materials;

    public string Id => _id;
    public float Size => _size;
    public float LifeTime => _lifetime;

	public Material GetMaterial()
    {
        return _materials.Length > 0 
            ? _materials.GetRandomElement() 
            : _materials.Length > 0 ? _materials[0] : null;
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
