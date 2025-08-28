// LeaderboardClient.cs (UniTask version)
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;
using Cysharp.Threading.Tasks;
using System.Threading;

public static class LeaderboardOrder
{
	public const string Desc = "desc"; // score DESC
	public const string Asc = "asc";  // score ASC
}

public sealed class LeaderboardClient
{
	private readonly string _baseUrl;   // e.g. http://185.162.11.233:8000
	private string _apiToken;           // X-Auth-Token

	/// <summary>Network timeout for each request.</summary>
	public int TimeoutSeconds { get; set; } = 15;

	public LeaderboardClient(string baseUrl, string apiToken)
	{
		_baseUrl = baseUrl.TrimEnd('/');
		_apiToken = apiToken;
	}

	public void SetToken(string apiToken) => _apiToken = apiToken;

	// -------------------- API --------------------

	/// <summary>Create a new leaderboard row. Server fills 'date' if null.</summary>
	public async UniTask<ScoreEntry> AddScoreAsync(
		string name,
		int score,
		long timeMs,
		string date = null,
		string weapon = null,
		string country = null,
		string version = null,
		string deviceId = null,
		string clientEntryId = null,
		CancellationToken ct = default)
	{
		var payload = new Dictionary<string, object>
		{
			["name"] = name,
			["score"] = score,
			["timeMs"] = timeMs
		};
		AddIfNotEmpty(payload, "date", date);
		AddIfNotEmpty(payload, "weapon", weapon);
		AddIfNotEmpty(payload, "country", country);
		AddIfNotEmpty(payload, "version", version);
		AddIfNotEmpty(payload, "deviceId", deviceId);
		AddIfNotEmpty(payload, "clientEntryId", clientEntryId);

		return await PostJsonAsync<ScoreEntry>("/scores", payload, ct);
	}

	private static string NormalizeOrder(string order)
	{
		if (string.Equals(order, LeaderboardOrder.Desc, StringComparison.OrdinalIgnoreCase)) return LeaderboardOrder.Desc;
		if (string.Equals(order, LeaderboardOrder.Asc, StringComparison.OrdinalIgnoreCase)) return LeaderboardOrder.Asc;
		// back-compat:
		if (string.Equals(order, "score_desc", StringComparison.OrdinalIgnoreCase)) return LeaderboardOrder.Desc;
		if (string.Equals(order, "score_asc", StringComparison.OrdinalIgnoreCase)) return LeaderboardOrder.Asc;
		return LeaderboardOrder.Desc;
	}

	/// <summary>Get list sorted only by score (desc|asc).</summary>
	public async UniTask<List<ScoreEntry>> GetScoresAsync(
		int limit = 100,
		string order = LeaderboardOrder.Desc,
		int offset = 0,
		CancellationToken ct = default)
	{
		order = NormalizeOrder(order); // <Ч добавили
		var url = $"{_baseUrl}/scores?limit={limit}&offset={offset}&order={order}";
		using var req = UnityWebRequest.Get(url);
		req.timeout = TimeoutSeconds;

		try
		{
			await req.SendWebRequest()
					 .ToUniTask(cancellationToken: ct)
					 .Timeout(TimeSpan.FromSeconds(TimeoutSeconds));

			if (req.result == UnityWebRequest.Result.Success)
			{
				return JsonConvert.DeserializeObject<List<ScoreEntry>>(req.downloadHandler.text)
					   ?? new List<ScoreEntry>();
			}

			LogHttpError("GET", url, req);
		}
		catch (OperationCanceledException) { throw; }
		catch (TimeoutException) { Debug.LogError("[Leaderboard] GET timeout"); }
		catch (Exception ex)
		{
			Debug.LogError($"[Leaderboard] GET exception: {ex.Message}");
		}
		return new List<ScoreEntry>();
	}

	/// <summary>Admin: clear database (dev only). Returns number of deleted rows.</summary>
	public async UniTask<int> ClearAllAsync(CancellationToken ct = default)
	{
		var res = await PostJsonAsync<ClearReply>("/admin/clear", new { }, ct);
		return res != null && res.ok ? res.deleted : -1;
	}

	// -------------------- internals --------------------

	private async UniTask<T> PostJsonAsync<T>(string path, object payload, CancellationToken ct)
	{
		var url = _baseUrl + path;
		var json = JsonConvert.SerializeObject(payload);

		using var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
		{
			uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
			downloadHandler = new DownloadHandlerBuffer()
		};
		req.SetRequestHeader("Content-Type", "application/json");
		if (!string.IsNullOrEmpty(_apiToken))
			req.SetRequestHeader("X-Auth-Token", _apiToken);
		req.timeout = TimeoutSeconds;

		try
		{
			await req.SendWebRequest()
					 .ToUniTask(cancellationToken: ct)
					 .Timeout(TimeSpan.FromSeconds(TimeoutSeconds));

			if (req.result == UnityWebRequest.Result.Success)
			{
				try
				{
					return JsonConvert.DeserializeObject<T>(req.downloadHandler.text);
				}
				catch (Exception ex)
				{
					Debug.LogError($"[Leaderboard] JSON parse error (POST {path}): {ex.Message}\nBody: {req.downloadHandler.text}");
					return default;
				}
			}

			LogHttpError("POST", url, req);
		}
		catch (OperationCanceledException) { throw; }
		catch (TimeoutException) { Debug.LogError($"[Leaderboard] POST {path} timeout"); }
		catch (Exception ex)
		{
			Debug.LogError($"[Leaderboard] POST {path} exception: {ex.Message}");
		}
		return default;
	}

	private static void AddIfNotEmpty(Dictionary<string, object> dict, string key, string value)
	{
		if (!string.IsNullOrEmpty(value)) dict[key] = value;
	}

	private void LogHttpError(string method, string url, UnityWebRequest req)
	{
		var body = req.downloadHandler != null ? req.downloadHandler.text : "<no body>";
		Debug.LogError($"[Leaderboard] {method} {url} failed: {(long)req.responseCode} {req.error}. Body: {body}");
	}

	[Serializable]
	private class ClearReply { public bool ok; public int deleted; }
}
