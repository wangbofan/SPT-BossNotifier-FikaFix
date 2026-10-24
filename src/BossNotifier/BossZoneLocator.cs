using System.Collections.Generic;
using BepInEx.Logging;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace BossNotifier;

/// <summary>
/// 客户端侧的 BotZone 反查。
///
/// 原版只在主机侧通过 <c>BossLocationSpawn.Init</c> 后置补丁拿到“boss + 出生区域”这一对数据。
/// Fika 客户端根本不执行出生逻辑，因此即使认出了 boss 也没有区域名，
/// 提示会退化成 “X has been located.”。这里用 boss 当前坐标反查最近的 BotZone，
/// 让客户端也能给出区域名。
/// </summary>
internal static class BossZoneLocator
{
	private static BotZone[] _zones;

	private static float _nextRefresh;

	public static void Reset()
	{
		_zones = null;
		_nextRefresh = 0f;
	}

	/// <summary>返回玩家友好的区域名，无法判断时返回空串。</summary>
	public static string GetZoneNameAt(Vector3 position)
	{
		BotZone zone = FindNearest(position);
		if ((Object)(object)zone == (Object)null)
		{
			return string.Empty;
		}
		string zoneId = zone.name;
		if (string.IsNullOrEmpty(zoneId))
		{
			return string.Empty;
		}
		return BossNotifierPlugin.GetZoneName(zoneId) ?? string.Empty;
	}

	private static BotZone[] GetZones()
	{
		if (_zones != null && _zones.Length != 0)
		{
			return _zones;
		}
		if (Time.realtimeSinceStartup < _nextRefresh)
		{
			return _zones;
		}
		_nextRefresh = Time.realtimeSinceStartup + 5f;
		List<BotZone> list = new List<BotZone>();
		try
		{
			foreach (BotZone item in LocationScene.GetAllObjects<BotZone>(withDisabled: true))
			{
				if (!((Object)(object)item == (Object)null))
				{
					list.Add(item);
				}
			}
		}
		catch (System.Exception ex)
		{
			BossNotifierPlugin.Log(LogLevel.Warning, "BotZone lookup failed: " + ex.Message);
		}
		if (list.Count != 0)
		{
			_zones = list.ToArray();
			BossNotifierPlugin.Log(LogLevel.Info, $"Cached {_zones.Length} BotZone(s) for client-side zone resolution");
		}
		return _zones;
	}

	private static BotZone FindNearest(Vector3 position)
	{
		BotZone[] zones = GetZones();
		if (zones == null || zones.Length == 0)
		{
			return null;
		}
		BotZone best = null;
		float bestDistance = float.MaxValue;
		for (int i = 0; i < zones.Length; i++)
		{
			BotZone botZone = zones[i];
			if ((Object)(object)botZone == (Object)null)
			{
				continue;
			}
			if (string.IsNullOrEmpty(BossNotifierPlugin.GetZoneName(botZone.name)))
			{
				continue;
			}
			float distance = DistanceTo(botZone, position);
			if (distance < bestDistance)
			{
				bestDistance = distance;
				best = botZone;
			}
		}
		return best;
	}

	private static float DistanceTo(BotZone zone, Vector3 position)
	{
		float num = float.MaxValue;
		List<SpawnPointMarker> markers = zone.SpawnPointMarkers;
		if (markers != null && markers.Count > 0)
		{
			for (int i = 0; i < markers.Count; i++)
			{
				SpawnPointMarker spawnPointMarker = markers[i];
				if ((Object)(object)spawnPointMarker == (Object)null)
				{
					continue;
				}
				float magnitude = (spawnPointMarker.transform.position - position).magnitude;
				if (magnitude < num)
				{
					num = magnitude;
				}
			}
			if (num < float.MaxValue)
			{
				return num;
			}
		}
		Vector3 centerOfSpawnPoints = zone.CenterOfSpawnPoints;
		if (centerOfSpawnPoints != Vector3.zero)
		{
			return (centerOfSpawnPoints - position).magnitude;
		}
		Transform transform = zone.transform;
		if ((Object)(object)transform != (Object)null)
		{
			return (transform.position - position).magnitude;
		}
		return float.MaxValue;
	}
}
