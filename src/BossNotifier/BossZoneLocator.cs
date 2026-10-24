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

		// 首选：直接读 LocationScene.BotZones 字段。
		// 不能用 LocationScene.GetAllObjects<BotZone>()：它查的是 _typeToContainer，
		// 而 LocationScene 里的 Register(BotZones) 可能在地形数组填充之前执行，
		// 于是登记进容器的是个空数组，之后不会再更新 —— 实测在 Streets 上永远返回 0 个 zone。
		int sceneZones = 0;
		try
		{
			List<LocationScene> scenes = LocationScene.LoadedScenes;
			if (scenes != null)
			{
				for (int s = 0; s < scenes.Count; s++)
				{
					LocationScene scene = scenes[s];
					if ((Object)(object)scene == (Object)null)
					{
						continue;
					}
					BotZone[] array = scene.BotZones;
					if (array == null)
					{
						continue;
					}
					for (int i = 0; i < array.Length; i++)
					{
						BotZone zone = array[i];
						if (!((Object)(object)zone == (Object)null))
						{
							list.Add(zone);
						}
					}
				}
			}
			sceneZones = list.Count;
		}
		catch (System.Exception ex)
		{
			BossNotifierPlugin.Log(LogLevel.Warning, "LocationScene.BotZones lookup failed: " + ex.Message);
		}

		// 兜底：直接扫已加载对象（含未激活），不依赖场景登记表。
		if (list.Count == 0)
		{
			try
			{
				BotZone[] all = Resources.FindObjectsOfTypeAll<BotZone>();
				if (all != null)
				{
					for (int i = 0; i < all.Length; i++)
					{
						BotZone zone = all[i];
						if (!((Object)(object)zone == (Object)null))
						{
							list.Add(zone);
						}
					}
				}
			}
			catch (System.Exception ex2)
			{
				BossNotifierPlugin.Log(LogLevel.Warning, "FindObjectsOfTypeAll<BotZone> failed: " + ex2.Message);
			}
		}

		if (list.Count != 0)
		{
			_zones = list.ToArray();
			BossNotifierPlugin.Log(LogLevel.Info, $"Cached {_zones.Length} BotZone(s) for zone resolution (scene field: {sceneZones})");
		}
		else
		{
			BossNotifierPlugin.Log(LogLevel.Warning, "No BotZone found yet - zone names unavailable, will retry");
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
			if (string.IsNullOrEmpty(botZone.name))
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
