using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen red veil for the cinematic fail sequence. Created at runtime
/// (its own Canvas above the game, but below the defeat window) and driven by the CanvasGroup alpha.
/// raycastTarget/blocksRaycasts are off — the veil never intercepts menu clicks.
/// Lives in the active scene, so it's destroyed on scene reload (restart).
/// </summary>
public sealed class FailScreenOverlay
{
	// Below the defeat window's sortingOrder (FailWindow.Show raises it higher), so the menu stays on top.
	private const int SortingOrder = 1000;

	private CanvasGroup _group;

	public void EnsureCreated(Color color)
	{
		if (_group != null)
			return;

		var go = new GameObject("FailScreenOverlay");
		var canvas = go.AddComponent<Canvas>();
		canvas.renderMode = RenderMode.ScreenSpaceOverlay;
		canvas.sortingOrder = SortingOrder;

		_group = go.AddComponent<CanvasGroup>();
		_group.alpha = 0f;
		_group.interactable = false;
		_group.blocksRaycasts = false;

		var tintGo = new GameObject("Tint");
		tintGo.transform.SetParent(go.transform, false);
		var image = tintGo.AddComponent<Image>();
		image.color = color;
		image.raycastTarget = false;

		var rt = image.rectTransform;
		rt.anchorMin = Vector2.zero;
		rt.anchorMax = Vector2.one;
		rt.offsetMin = Vector2.zero;
		rt.offsetMax = Vector2.zero;
	}

	public void SetAlpha(float alpha)
	{
		if (_group != null)
			_group.alpha = Mathf.Clamp01(alpha);
	}
}
