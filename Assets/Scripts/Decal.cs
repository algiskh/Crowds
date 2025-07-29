using System;
using UnityEditor.VersionControl;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Decal : MonoBehaviour
{
	private string _id;
	public string Id => _id;

	public void Initialize(DecalConfig config)
	{
		_id = config.Id;
		var projector = GetComponent<DecalProjector>();
		if (projector == null)
		{
			throw new NullReferenceException("Failed to find decal projector in prefab");
		}
		transform.localScale = Vector3.one * config.Size;
		projector.material = config.GetMaterial();
	}

	public void Show()
	{
		gameObject.SetActive(true);
	}

	public void Hide()
	{
		gameObject.SetActive(false);
	}
}
