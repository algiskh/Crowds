// LeaderboardDemo.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class LeaderboardDemo : MonoBehaviour
{
	// ===== Server =====
	[TitleGroup("Server")]
	[LabelText("Base URL")][SerializeField] private string baseUrl = "http://185.162.11.233:8001"; // 127.0.0.1
	[TitleGroup("Server")]
	[LabelText("API Token")][SerializeField] private string apiToken = "SUPER_SECRET_TOKEN";

	// ===== Identity =====
	[TitleGroup("Identity")]
	[ToggleLeft]
	[LabelText("Auto deviceId (SystemInfo / GUID)")][SerializeField] private bool autoDeviceId = true;

	[TitleGroup("Identity")]
	[LabelText("deviceId override")]
	[SerializeField, DisableIf(nameof(autoDeviceId))]
	private string deviceIdManual = "";

	[TitleGroup("Identity")]
	[LabelText("Bucket (for clientEntryId)")][SerializeField] private string bucket = "global";

	[TitleGroup("Identity"), ShowInInspector, ReadOnly]
	[LabelText("Computed clientEntryId")] private string clientEntryIdPreview = "—";

	[TitleGroup("Identity"), Button(ButtonSizes.Small)]
	private void RegenerateDeviceGuid()
	{
		var guid = System.Guid.NewGuid().ToString("N");
		PlayerPrefs.SetString("lb_device_guid", guid);
		PlayerPrefs.Save();
		UpdateClientEntryPreview();
	}

	// ===== Entry =====
	[TitleGroup("Entry")][LabelText("Player Name")][SerializeField] private string playerName = "Ivan";
	[TitleGroup("Entry")][LabelText("Score")][MinValue(0)][SerializeField] private int score = 300;
	[TitleGroup("Entry")][LabelText("Time (ms)")][MinValue(0)][SerializeField] private long timeMs = 74321;

	[TitleGroup("Entry")]
	[ToggleLeft]
	[LabelText("Use server date (now)")][SerializeField] private bool useServerDate = true;

	// server expects "HH:MM:SS dd.MM.yyyy" if you send custom date
	[TitleGroup("Entry")]
	[ShowIf("@!useServerDate")]
	[LabelText("Date (HH:MM:SS dd.MM.yyyy)")][SerializeField] private string dateOverride = "";

	[TitleGroup("Entry")][LabelText("Weapon")][SerializeField] private string weapon = "AK";
	[TitleGroup("Entry")][LabelText("Country (ISO2)")][SerializeField] private string country = ""; // will auto-fill
	[TitleGroup("Entry")][LabelText("Version")][SerializeField] private string versionStr = "";

	// ===== Controls =====
	[TitleGroup("Controls")]
	[HorizontalGroup("Controls/Timer"), Button(ButtonSizes.Medium), GUIColor(0.8f, 1f, 0.8f)]
	private void StartRun() { _sw.Restart(); }

	[HorizontalGroup("Controls/Timer"), Button(ButtonSizes.Medium), GUIColor(1f, 0.9f, 0.6f)]
	private void StopAndSetTime()
	{
		_sw.Stop();
		timeMs = _sw.ElapsedMilliseconds;
	}

	[TitleGroup("Controls")]
	[HorizontalGroup("Controls/Actions"), Button("Submit", ButtonSizes.Medium),
	 DisableIf(nameof(_isBusy)), GUIColor(0.7f, 0.9f, 1f)]
	private void Submit() => SubmitAsync(this.GetCancellationTokenOnDestroy()).Forget();

	[TitleGroup("Controls")]
	[HorizontalGroup("Controls/Actions"), Button("Refresh", ButtonSizes.Medium),
	 DisableIf(nameof(_isBusy)), GUIColor(0.7f, 1f, 0.7f)]
	private void RefreshTop() => RefreshAsync(this.GetCancellationTokenOnDestroy()).Forget();

	[TitleGroup("Controls")]
	[HorizontalGroup("Controls/Actions"), Button("Clear DB", ButtonSizes.Medium),
	 DisableIf(nameof(_isBusy)), GUIColor(1f, 0.6f, 0.6f)]
	private void AskClearDb()
	{
		// show confirmation flag (Odin) and also runtime popup
		_askClear = true;
		_showRuntimeConfirm = true;
	}

	// ===== Update target (optional for visibility) =====
	[TitleGroup("Controls")]
	[LabelText("Last Status")]
	[ShowInInspector, ReadOnly]
	private string lastStatus = "—";

	// ===== Top list =====
	[FoldoutGroup("Top List")]
	[LabelText("Limit")][MinValue(1)][MaxValue(1000)] public int topLimit = 10;

	[FoldoutGroup("Top List")]
	[LabelText("Order")][ValueDropdown(nameof(OrderOptions))] public string order = LeaderboardOrder.Desc;

	[FoldoutGroup("Top List")]
	[TableList(IsReadOnly = true, AlwaysExpanded = true)]
	public List<ScoreEntry> top = new();

	// ===== Confirmation block (Odin runtime inspector) =====
	[FoldoutGroup("Confirm"), ShowIf(nameof(_askClear))]
	[InfoBox("This will delete ALL rows from the leaderboard on the server.", InfoMessageType.Warning)]
	[HorizontalGroup("Confirm/Btns")]
	[Button("Yes, clear"), GUIColor(1f, 0.4f, 0.4f)]
	private void ConfirmClearYes() => DoClearAsync(this.GetCancellationTokenOnDestroy()).Forget();

	[HorizontalGroup("Confirm/Btns")]
	[Button("Cancel"), GUIColor(0.8f, 0.8f, 0.8f)]
	private void ConfirmClearNo() { _askClear = false; _showRuntimeConfirm = false; }

	// ===== internals =====
	private LeaderboardClient client;
	private bool _isBusy;
	private bool _askClear;               // shows Odin confirm block
	private bool _showRuntimeConfirm;     // draws small OnGUI confirm in builds
	private readonly Stopwatch _sw = new Stopwatch();

	private IEnumerable<string> OrderOptions => new[] { LeaderboardOrder.Desc, LeaderboardOrder.Asc };

	private void Awake()
	{
		EnsureClient();
		if (string.IsNullOrEmpty(versionStr)) versionStr = Application.version;
		if (string.IsNullOrEmpty(country)) country = TryGetIsoCountry() ?? "";
		MigrateOrder();
		UpdateClientEntryPreview();
	}

	private void OnValidate()
	{
		MigrateOrder();
		UpdateClientEntryPreview();
	}

	private void MigrateOrder()
	{
		// Приводим старые значения к новому API
		if (string.Equals(order, "score_desc", StringComparison.OrdinalIgnoreCase))
			order = LeaderboardOrder.Desc;
		else if (string.Equals(order, "score_asc", StringComparison.OrdinalIgnoreCase))
			order = LeaderboardOrder.Asc;
		// Любые другие — по умолчанию DESC
		else if (!string.Equals(order, LeaderboardOrder.Desc, StringComparison.OrdinalIgnoreCase) &&
				 !string.Equals(order, LeaderboardOrder.Asc, StringComparison.OrdinalIgnoreCase))
			order = LeaderboardOrder.Desc;
	}
	private void EnsureClient()
	{
		if (client == null) client = new LeaderboardClient(baseUrl.TrimEnd('/'), apiToken);
	}

	private string GetDeviceId()
	{
		if (!autoDeviceId) return deviceIdManual?.Trim();

		var id = SystemInfo.deviceUniqueIdentifier;
		if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier)
		{
			if (!PlayerPrefs.HasKey("lb_device_guid"))
				PlayerPrefs.SetString("lb_device_guid", System.Guid.NewGuid().ToString("N"));
			id = PlayerPrefs.GetString("lb_device_guid");
		}
		return id;
	}

	private static string MakeClientEntryId(string deviceId, string name, string bucket)
	{
		using var sha = SHA256.Create();
		var raw = $"{deviceId}|{name}|{bucket}";
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
		var sb = new StringBuilder(32);
		for (int i = 0; i < 16; i++) sb.Append(hash[i].ToString("x2"));
		return sb.ToString();
	}

	private void UpdateClientEntryPreview()
	{
		var dev = GetDeviceId();
		var nm = string.IsNullOrWhiteSpace(playerName) ? "player" : playerName.Trim();
		clientEntryIdPreview = string.IsNullOrEmpty(dev) ? "—" : MakeClientEntryId(dev, nm, bucket);
	}

	private static string TryGetIsoCountry()
	{
		try
		{
			var name = CultureInfo.CurrentCulture.Name; // e.g., "sr-RS"
			if (!string.IsNullOrEmpty(name))
			{
				var ri = new RegionInfo(name);
				var iso = ri.TwoLetterISORegionName;
				if (!string.IsNullOrEmpty(iso) && iso.Length == 2)
					return iso.ToUpperInvariant();
			}
		}
		catch { }
		return null;
	}

	// ---------- Actions ----------

	private async UniTaskVoid SubmitAsync(CancellationToken ct)
	{
		EnsureClient();
		_isBusy = true;
		try
		{
			var date = useServerDate ? null : (string.IsNullOrWhiteSpace(dateOverride) ? null : dateOverride.Trim());
			var devId = GetDeviceId();
			var ceid = string.IsNullOrEmpty(devId) ? null : MakeClientEntryId(devId, playerName.Trim(), bucket);

			var created = await client.AddScoreAsync(
				name: playerName.Trim(),
				score: score,
				timeMs: timeMs,
				date: date,
				weapon: weapon,
				country: string.IsNullOrWhiteSpace(country) ? null : country.Trim().ToUpperInvariant(),
				version: versionStr,
				deviceId: devId,
				clientEntryId: ceid,
				ct: ct
			);

			bool ok = created != null;
			lastStatus = ok
				? $"Submitted ✓ (id={created.id}, {created.name} score={created.score} time={created.timeMs}ms)"
				: "Submit failed ✗";

			if (ok) await RefreshAsync(ct);
		}
		catch (OperationCanceledException) { /* ignore */ }
		catch (Exception ex) { lastStatus = "Error: " + ex.Message; Debug.LogError(ex); }
		finally { _isBusy = false; }
	}

	private async UniTask RefreshAsync(CancellationToken ct)
	{
		EnsureClient();
		_isBusy = true;
		try
		{
			top = await client.GetScoresAsync(topLimit, order, 0, ct);
			lastStatus = $"Loaded {top.Count} rows";
		}
		catch (OperationCanceledException) { /* ignore */ }
		catch (Exception ex) { lastStatus = "Error: " + ex.Message; Debug.LogError(ex); }
		finally { _isBusy = false; }
	}

	private async UniTaskVoid DoClearAsync(CancellationToken ct)
	{
		EnsureClient();
		_isBusy = true;
		try
		{
			var deleted = await client.ClearAllAsync(ct);
			_askClear = false;
			_showRuntimeConfirm = false;

			if (deleted >= 0)
			{
				lastStatus = $"Cleared ✓ (deleted={deleted})";
				await RefreshAsync(ct);
			}
			else
			{
				lastStatus = "Clear failed ✗";
			}
		}
		catch (OperationCanceledException) { /* ignore */ }
		catch (Exception ex) { lastStatus = "Error: " + ex.Message; Debug.LogError(ex); }
		finally { _isBusy = false; }
	}

	// ---------- Minimal runtime confirmation (for builds) ----------
	// Press F10 to toggle panel in build if needed.
	private Rect confirmRect = new Rect(20, 20, 360, 140);

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F10))
			_showRuntimeConfirm = !_showRuntimeConfirm;
	}

	private void OnGUI()
	{
		if (!_showRuntimeConfirm) return;

		confirmRect = GUI.ModalWindow(987654, confirmRect, id =>
		{
			GUILayout.Label("Are you sure you want to CLEAR ALL rows?", GUILayout.Height(32));
			GUILayout.Space(10);
			GUILayout.BeginHorizontal();
			if (GUILayout.Button("Yes, clear", GUILayout.Height(28)))
			{
				DoClearAsync(this.GetCancellationTokenOnDestroy()).Forget();
			}
			if (GUILayout.Button("Cancel", GUILayout.Height(28)))
			{
				_askClear = false;
				_showRuntimeConfirm = false;
			}
			GUILayout.EndHorizontal();
		}, "Confirm");
	}
}
