using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Полноэкранная красная пелена для кинематографичной концовки. Создаётся в рантайме
/// (свой Canvas поверх игры, но под окном поражения) и управляется альфой CanvasGroup.
/// raycastTarget/blocksRaycasts выключены — пелена никогда не перехватывает клики по меню.
/// Живёт в активной сцене, поэтому уничтожается при её перезагрузке (рестарт).
/// </summary>
public sealed class FailScreenOverlay
{
	// Ниже, чем sortingOrder окна поражения (FailWindow.Show поднимает его выше), чтобы меню было сверху.
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
