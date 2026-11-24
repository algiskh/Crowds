using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;

/// <summary>
/// Old-style seven-segment scoreboard ("HOME:AWAY"). Each digit is 7 segment renderers (a b c d e f g order, see
/// <see cref="DigitMasks"/>) that swap between a lit and an unlit material. 3 + 3 cells; the numbers grow outward
/// from the colon (home right-aligned against it, away left-aligned after it), unused cells stay dark ("8" off):
/// 0 deaths / 51 frags → "  0:51 ", 1 / 81 → "  1:81 ". Built by HockeyLevelBuilder; driven by ECS.ScoreboardSystem
/// (home = player deaths, away = frags, or the breather countdown in seconds). Static registry like BulletBlocker
/// (OnEnable/OnDisable).
/// </summary>
public sealed class ArenaScoreboard : MonoBehaviour
{
	// Bit i = segment i (a top, b top-right, c bottom-right, d bottom, e bottom-left, f top-left, g middle).
	private static readonly byte[] DigitMasks = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
	public const int SegmentsPerDigit = 7;

	private static readonly List<ArenaScoreboard> ActiveList = new();
	public static List<ArenaScoreboard> Active => ActiveList;

	[BoxGroup("Segments"), Tooltip("Home digits × 7 renderers, digit-major, segment order a…g.")]
	[SerializeField] private Renderer[] _homeSegments;
	[BoxGroup("Segments"), Tooltip("Away digits × 7 renderers, digit-major, segment order a…g.")]
	[SerializeField] private Renderer[] _awaySegments;

	[BoxGroup("Materials"), Required]
	[SerializeField] private Material _litMaterial;
	[BoxGroup("Materials"), Required]
	[SerializeField] private Material _offMaterial;

	[BoxGroup("Format"), Tooltip("Home side shows nothing while the value is 0 (no deaths yet).")]
	[SerializeField] private bool _homeBlankZero;
	[BoxGroup("Format"), Tooltip("Away side shows nothing while the value is 0 (off = a single '0').")]
	[SerializeField] private bool _awayBlankZero;

	private int _home = -1, _away = -1;

	private void OnEnable() => ActiveList.Add(this);
	private void OnDisable() => ActiveList.Remove(this);

	public void SetScore(int home, int away)
	{
		if (home == _home && away == _away)
			return;
		_home = home;
		_away = away;
		Write(_homeSegments, home, alignRight: true, _homeBlankZero);
		Write(_awaySegments, away, alignRight: false, _awayBlankZero);
	}

	// No leading zeros: the digits sit against the colon (home right-aligned, away left-aligned), the rest of the
	// cells are dark. Values that don't fit are clamped to all nines.
	private void Write(Renderer[] segments, int value, bool alignRight, bool blankZero)
	{
		if (segments == null)
			return;
		int cells = segments.Length / SegmentsPerDigit;
		if (cells == 0)
			return;
		value = Mathf.Max(0, value);
		int max = 1;
		for (int i = 0; i < cells; i++) max *= 10;
		value = Mathf.Min(value, max - 1);

		string text = blankZero && value == 0 ? string.Empty : value.ToString();
		int start = alignRight ? cells - text.Length : 0; // first cell holding a digit
		for (int cell = 0; cell < cells; cell++)
		{
			int index = cell - start;
			int mask = index >= 0 && index < text.Length ? DigitMasks[text[index] - '0'] : 0;
			for (int s = 0; s < SegmentsPerDigit; s++)
			{
				var r = segments[cell * SegmentsPerDigit + s];
				if (r != null)
					r.sharedMaterial = (mask & (1 << s)) != 0 ? _litMaterial : _offMaterial;
			}
		}
	}

	[Button("Preview  0:51")]
	private void PreviewScore() { _home = _away = -1; SetScore(0, 51); }
}
