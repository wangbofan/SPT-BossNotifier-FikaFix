using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using SPT.Reflection.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BossNotifier;

public class BossNotifierMono : MonoBehaviour
{
	public static BossNotifierMono Instance;

	private List<string> bossNotificationMessages;

	public int intelCenterLevel;

	private GameWorld _gameWorld;

	private Dictionary<Player, BossMarkerInfo> _bossMarkers = new Dictionary<Player, BossMarkerInfo>();

	private bool _markersVisible = true;

	private Camera _mainCamera;

	private readonly HashSet<string> _announcedGroups = new HashSet<string>();

	private float _raidStartTime;

	private bool _raidStartAnnounced;

	/// <summary>UI（提示 / 屏幕标记）路径是否已初始化。无头服务端永远为 false。</summary>
	private bool _uiReady;

	/// <summary>等待本地玩家出现的截止时间，超过就判定本进程没有 HUD。</summary>
	private float _uiWaitDeadline;

	/// <summary>客户端接近 boss 时触发 “detected in your vicinity” 提示的距离（米）。</summary>
	private const float VicinityDetectDistance = 60f;

	/// <summary>
	/// 本进程已经看到过多少个非 PMC（AI）实体。大于 0 说明 bot 出生包已经到货，
	/// boss 名单可以直接定论，不用再等到超时。
	/// </summary>
	private int _botObservations;

	/// <summary>是否已经为“数据到货”排过一次即时播报，避免同一波 bot 反复重排。</summary>
	private bool _announceScheduled;

	/// <summary>
	/// 联机客户端在开局后等待 bot 出生数据的时长（秒）。
	/// 超过这个时长仍然没有 boss 数据，就播 “No Bosses Located”。
	/// 必须明显短于一场战局的时长，否则玩家等不到任何提示。
	/// </summary>
	private const float BossDataWaitSeconds = 15f;

	/// <summary>boss 数据等待阶段的重试间隔（秒）。</summary>
	private const float BossDataRetryInterval = 2f;

	public static void Init()
	{
		if (!Singleton<GameWorld>.Instantiated)
		{
			return;
		}
		Instance = GameObjectExtensions.GetOrAddComponent<BossNotifierMono>((MonoBehaviour)(object)Singleton<GameWorld>.Instance);
		BossNotifierPlugin.Log((LogLevel)16, "Game started on map " + Singleton<GameWorld>.Instance.LocationId);
		try
		{
			AreaInfo[] areas = ((IProfileSession)((ClientApplication<IEftSession>)(object)ClientAppUtils.GetMainApp()).GetClientBackEndSession()).Profile.Hideout.Areas;
			if (areas != null)
			{
				AreaInfo val = areas.FirstOrDefault((AreaInfo a) =>
				{
					//IL_0001: Unknown result type (might be due to invalid IL or missing references)
					//IL_0008: Invalid comparison between Unknown and I4
					return (int)a.AreaType == 11;
				});
				Instance.intelCenterLevel = val?.Level ?? 0;
			}
			else
			{
				Instance.intelCenterLevel = 0;
			}
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)4, "Could not get Intel Center level: " + ex.Message);
			Instance.intelCenterLevel = 0;
		}
		BossNotifierPlugin.Log((LogLevel)16, $"Intel Center level: {Instance.intelCenterLevel}");
	}

	public void Start()
	{
		_gameWorld = Singleton<GameWorld>.Instance;
		FikaCompat.Resolve();
		if ((Object)(object)_gameWorld != (Object)null)
		{
			_gameWorld.OnPersonAdd += OnPersonAdd;
			BossNotifierPlugin.Log((LogLevel)16, "Registered OnPersonAdd event");
		}
		BossNotifierPlugin.InvokeOnRaidStarted();
		_uiWaitDeadline = Time.realtimeSinceStartup + 120f;
		TryBeginUi();
	}

	/// <summary>
	/// 等本地玩家出现后再初始化 UI 路径。
	///
	/// 判据是“本进程有没有本地玩家”（<see cref="FikaCompat.CanRenderUi"/>），
	/// 而不是 Fika 的 ClientType：Fika 客户端上 ClientType 在进战局前是 None，
	/// 而 IsHeadlessGame 表示的是“房主是无头服务端”，加入无头房间的客户端同样是 true，
	/// 用它当开关会把正常客户端也一起静默。
	/// </summary>
	private void TryBeginUi()
	{
		if (_uiReady)
		{
			return;
		}
		if (!FikaCompat.CanRenderUi)
		{
			if (Time.realtimeSinceStartup > _uiWaitDeadline)
			{
				BossNotifierPlugin.Log((LogLevel)16, $"No local player in this process (localHeadless={FikaCompat.LocalHeadless}, clientType={FikaCompat.ClientType}) - boss tracking stays passive");
				return;
			}
			((MonoBehaviour)this).Invoke("TryBeginUi", 0.5f);
			return;
		}
		_uiReady = true;
		_raidStartTime = Time.realtimeSinceStartup;
		_mainCamera = Camera.main;
		_markersVisible = BossNotifierPlugin.enableMarkers.Value;
		BossNotifierPlugin.Log((LogLevel)16, $"BossNotifier UI ready (clientType={FikaCompat.ClientType}, hostHeadless={FikaCompat.HostIsHeadless})");
		InitializeExistingBossMarkers();
		GenerateBossNotifications();
		((MonoBehaviour)this).Invoke("SendSelfTestNotification", 1f);
		if (BossNotifierPlugin.showNotificationsOnRaidStart.Value)
		{
			((MonoBehaviour)this).Invoke("SendBossNotifications", 2f);
		}
	}

	private void InitializeExistingBossMarkers()
	{
		try
		{
			InitializeExistingBossMarkersInternal();
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)2, "InitializeExistingBossMarkers failed: " + ex);
		}
	}

	private void InitializeExistingBossMarkersInternal()
	{
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Expected Obj, but got Unknown
		//IL_0104: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_gameWorld == (Object)null || _gameWorld.AllAlivePlayersList == null)
		{
			return;
		}
		int scanned = 0;
		int bosses = 0;
		foreach (Player allAlivePlayers in _gameWorld.AllAlivePlayersList)
		{
			if ((Object)(object)allAlivePlayers == (Object)null || allAlivePlayers.IsYourPlayer)
			{
				continue;
			}
			scanned++;
			Profile profile = allAlivePlayers.Profile;
			WildSpawnType? val;
			if (profile == null)
			{
				val = null;
			}
			else
			{
				ProfileInfo info = profile.Info;
				val = ((info == null) ? ((WildSpawnType?)null) : info.Settings?.Role);
			}
			WildSpawnType? val2 = val;
			if (val2.HasValue && BossNotifierPlugin.IsBoss(val2.Value))
			{
				string bossName = BossNotifierPlugin.GetBossName(val2.Value);
				string bossDisplayName = BossNotifierPlugin.GetBossDisplayName(val2.Value);
				if (bossName != null && !_bossMarkers.ContainsKey(allAlivePlayers))
				{
					bosses++;
					CreateBossMarker(allAlivePlayers, bossDisplayName ?? bossName, bossName);
					allAlivePlayers.OnPlayerDeadOrUnspawn += OnBossDeadOrUnspawn;
					RegisterBossInRaid(allAlivePlayers, val2.Value, bossName);
				}
			}
		}
		BossNotifierPlugin.Log((LogLevel)16, $"Scanned {scanned} existing player(s), found {bosses} boss(es)");
	}

	/// <summary>
	/// 把“已经在场上的 boss”登记进 <see cref="BossLocationSpawnPatch.bossesInRaid"/>。
	///
	/// 主机侧这条路通常由 <see cref="BossLocationSpawnPatch"/>（带 BornZone）或
	/// <see cref="BotBossPatch"/>（BotBoss 构造）先填好，这里只在缺失时兜底；
	/// Fika 客户端上两者都不会执行，因此这条路就是客户端唯一的 boss 数据来源，
	/// 区域名用 boss 实际坐标反查最近的 BotZone 得到。
	/// </summary>
	private void RegisterBossInRaid(Player player, WildSpawnType role, string groupName)
	{
		if (groupName == null)
		{
			return;
		}
		if (BossLocationSpawnPatch.bossesInRaid.ContainsKey(groupName))
		{
			return;
		}
		string zone = string.Empty;
		if ((Object)(object)player != (Object)null)
		{
			zone = BossZoneLocator.GetZoneNameAt(player.Position);
		}
		BossLocationSpawnPatch.bossesInRaid[groupName] = zone ?? string.Empty;
		BotBossPatch.spawnedBosses.Add(groupName);
		BossNotifierPlugin.Log((LogLevel)16, $"Registered {groupName} in bossesInRaid from local player list (zone: {(string.IsNullOrEmpty(zone) ? "<unknown>" : zone)}, role: {role})");
		GenerateBossNotifications();
		AnnounceGroup(groupName);
	}

	/// <summary>
	/// Fika 客户端在开局 2 秒后通常还没收到 bot 出生包，raid-start 提示会错过。
	/// 这里对“新出现的 boss”补一条提示（每组只补一次）。
	/// </summary>
	private void AnnounceGroup(string groupName)
	{
		string key = (BossNotifierPlugin.goonMembers.Contains(groupName) ? "Goons" : groupName);
		// 开局提示还没播（客户端还在等 bot 出生包）时，把这一组交给开局那次统一播报；
		// 但如果玩家关掉了开局提示，就退化成“每个新 boss 单独播一条”。
		if (!_raidStartAnnounced && BossNotifierPlugin.showNotificationsOnRaidStart.Value)
		{
			return;
		}
		if (!_announcedGroups.Add(key))
		{
			return;
		}
		if (!ShouldFunction() || intelCenterLevel < BossNotifierPlugin.intelCenterUnlockLevel.Value)
		{
			return;
		}
		GenerateBossNotifications();
		if (bossNotificationMessages == null)
		{
			return;
		}
		string prefix = key + " ";
		foreach (string bossNotificationMessage in bossNotificationMessages)
		{
			if (bossNotificationMessage != null && bossNotificationMessage.StartsWith(prefix, StringComparison.Ordinal) && !bossNotificationMessage.Contains("eliminated"))
			{
				NotificationManager.DisplayMessageNotification(bossNotificationMessage, (ENotificationDurationType)1, (ENotificationIconType)0, (Color?)null);
				return;
			}
		}
	}

	private void OnPersonAdd(IPlayer iPlayer)
	{
		try
		{
			OnPersonAddInternal(iPlayer);
		}
		catch (Exception ex)
		{
			// 不能把异常抛回 GameWorld.RegisterPlayer，否则会打断 bot 的注册流程。
			BossNotifierPlugin.Log((LogLevel)2, "OnPersonAdd failed: " + ex);
		}
	}

	private void OnPersonAddInternal(IPlayer iPlayer)
	{
		//IL_0056: Unknown result type (might be due to invalid IL or missing references)
		//IL_006d: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f3: Expected Obj, but got Unknown
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		Player val = (Player)(object)((iPlayer is Player) ? iPlayer : null);
		if ((Object)(object)val == (Object)null || val.IsYourPlayer)
		{
			return;
		}
		Profile profile = val.Profile;
		WildSpawnType? val2;
		if (profile == null)
		{
			val2 = null;
		}
		else
		{
			ProfileInfo info = profile.Info;
			val2 = ((info == null) ? ((WildSpawnType?)null) : info.Settings?.Role);
		}
		WildSpawnType? val3 = val2;
		// 只要有非 PMC（也就是 AI）进图，就说明 bot 出生包已经到了，boss 名单此刻就能定论，
		// 不必再干等满 <see cref="BossDataWaitSeconds"/>。
		if (!val3.HasValue || ((int)val3.Value != 51 && (int)val3.Value != 52))
		{
			_botObservations++;
			NotifyBotDataArrived();
		}
		if (val3.HasValue && BossNotifierPlugin.IsBoss(val3.Value))
		{
			string bossName = BossNotifierPlugin.GetBossName(val3.Value);
			string bossDisplayName = BossNotifierPlugin.GetBossDisplayName(val3.Value);
			if (bossName != null)
			{
				BossNotifierPlugin.Log((LogLevel)16, "Boss detected via OnPersonAdd: " + (bossDisplayName ?? bossName) + " (" + bossName + ")");
				CreateBossMarker(val, bossDisplayName ?? bossName, bossName);
				val.OnPlayerDeadOrUnspawn += OnBossDeadOrUnspawn;
				RegisterBossInRaid(val, val3.Value, bossName);
			}
		}
	}

	private void CreateBossMarker(Player player, string bossName, string groupName)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected Obj, but got Unknown
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Unknown result type (might be due to invalid IL or missing references)
		//IL_004a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0050: Unknown result type (might be due to invalid IL or missing references)
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_0098: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_0121: Unknown result type (might be due to invalid IL or missing references)
		if (_bossMarkers.ContainsKey(player))
		{
			return;
		}
		try
		{
			string markerCharacter = BossNotifierPlugin.GetMarkerCharacter(groupName);
			Color markerColor = BossNotifierPlugin.GetMarkerColor(groupName);
			GameObject val = new GameObject("BossMarker_" + bossName);
			GameObject val2 = new GameObject("Symbol");
			val2.transform.SetParent(val.transform);
			val2.transform.localPosition = Vector3.zero;
			TextMesh val3 = val2.AddComponent<TextMesh>();
			val3.text = markerCharacter;
			val3.fontSize = BossNotifierPlugin.fontSize.Value;
			val3.anchor = (TextAnchor)4;
			val3.alignment = (TextAlignment)1;
			val3.color = markerColor;
			val3.fontStyle = (FontStyle)1;
			GameObject val4 = new GameObject("Info");
			val4.transform.SetParent(val.transform);
			val4.transform.localPosition = new Vector3(0f, 0.05f, 0f);
			TextMesh val5 = val4.AddComponent<TextMesh>();
			val5.text = bossName;
			val5.fontSize = 48;
			val5.anchor = (TextAnchor)7;
			val5.alignment = (TextAlignment)1;
			val5.color = markerColor;
			val5.fontStyle = (FontStyle)1;
			val.AddComponent<Billboard>();
			val.transform.localScale = Vector3.one * BossNotifierPlugin.markerBaseScale.Value;
			UpdateMarkerPosition(val, player);
			val.SetActive(_markersVisible && BossNotifierPlugin.enableMarkers.Value);
			BossMarkerInfo value = new BossMarkerInfo(player, bossName, groupName, val, val3, val5);
			_bossMarkers[player] = value;
			if (BossNotifierPlugin.pluralBosses.Contains(groupName) || BossNotifierPlugin.customBossGroups.Contains(groupName))
			{
				BossNotifierPlugin.groupSeenCount.TryGetValue(groupName, out var value2);
				BossNotifierPlugin.groupSeenCount[groupName] = value2 + 1;
			}
			BossNotifierPlugin.Log((LogLevel)16, "Created marker for boss: " + bossName + " (group: " + groupName + ")");
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)2, "Failed to create marker for " + bossName + ": " + ex.Message);
		}
	}

	private void OnBossDeadOrUnspawn(Player player)
	{
		try
		{
			OnBossDeadOrUnspawnInternal(player);
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)2, "OnBossDeadOrUnspawn failed: " + ex);
		}
	}

	private void OnBossDeadOrUnspawnInternal(Player player)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_001c: Expected Obj, but got Unknown
		if ((Object)(object)player == (Object)null)
		{
			return;
		}
		player.OnPlayerDeadOrUnspawn -= OnBossDeadOrUnspawn;
		if (_bossMarkers.TryGetValue(player, out var value))
		{
			BossNotifierPlugin.Log((LogLevel)16, "Boss died/unspawned: " + value.BossName + " (group: " + value.GroupName + ")");
			if (BossNotifierPlugin.pluralBosses.Contains(value.GroupName) || BossNotifierPlugin.customBossGroups.Contains(value.GroupName))
			{
				BossNotifierPlugin.groupDeadCount.TryGetValue(value.GroupName, out var value2);
				BossNotifierPlugin.groupDeadCount[value.GroupName] = value2 + 1;
			}
			else
			{
				BotBossPatch.deadBosses.Add(value.GroupName);
			}
			BossNotifierPlugin.InvokeOnBossDied(value.GroupName);
			if ((Object)(object)value.MarkerObject != (Object)null)
			{
				Object.Destroy((Object)(object)value.MarkerObject);
			}
			_bossMarkers.Remove(player);
			GenerateBossNotifications();
		}
	}

	public void Update()
	{
		//IL_004f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		if (!_uiReady)
		{
			return;
		}
		if (!FikaCompat.IsHost)
		{
			// 非权威端（Fika 客户端 / 旁观）：bot 不会构造 BotBoss，vicinity 只能自己算。
			// 单机/房主侧由 BotBossPatch 负责，且两边共用 vicinityNotificationsSent 去重。
			UpdateClientVicinity();
		}
		if (BotBossPatch.vicinityNotifications.Count > 0)
		{
			string text = BotBossPatch.vicinityNotifications.Dequeue();
			if (Instance.intelCenterLevel >= BossNotifierPlugin.intelCenterDetectedUnlockLevel.Value)
			{
				NotificationManager.DisplayMessageNotification(text, (ENotificationDurationType)1, (ENotificationIconType)0, (Color?)null);
				Instance.GenerateBossNotifications();
			}
		}
		if (IsKeyPressed(BossNotifierPlugin.showBossesKeyCode.Value))
		{
			SendBossNotifications();
		}
		if (IsKeyPressed(BossNotifierPlugin.toggleMarkersKey.Value))
		{
			_markersVisible = !_markersVisible;
			ToggleAllMarkers(_markersVisible);
			string text2 = (_markersVisible ? "ON" : "OFF");
			NotificationManager.DisplayMessageNotification("Boss Markers: " + text2, (ENotificationDurationType)0, (ENotificationIconType)0, (Color?)null);
		}
		if (BossNotifierPlugin.enableMarkers.Value && _markersVisible)
		{
			UpdateAllMarkers();
		}
	}

	/// <summary>
	/// Fika 客户端专用的“附近有 boss”检测。
	/// 主机侧由 <see cref="BotBossPatch"/>（BotBoss 构造函数）负责，
	/// 客户端上的 boss 是 ObservedPlayer，不会触发那个补丁，所以这里按距离自己判定。
	/// </summary>
	private void UpdateClientVicinity()
	{
		if (_bossMarkers.Count == 0 || (Object)(object)_gameWorld == (Object)null)
		{
			return;
		}
		Player mainPlayer = _gameWorld.MainPlayer;
		if ((Object)(object)mainPlayer == (Object)null)
		{
			return;
		}
		if (BotBossPatch.vicinityNotifications.Count > 4)
		{
			return;
		}
		Vector3 origin = mainPlayer.Position;
		foreach (KeyValuePair<Player, BossMarkerInfo> bossMarker in _bossMarkers)
		{
			Player key = bossMarker.Key;
			BossMarkerInfo value = bossMarker.Value;
			if ((Object)(object)key == (Object)null || value == null)
			{
				continue;
			}
			string groupName = value.GroupName;
			if (string.IsNullOrEmpty(groupName))
			{
				continue;
			}
			string text = (BossNotifierPlugin.goonMembers.Contains(groupName) ? "Goons" : groupName);
			if (BotBossPatch.vicinityNotificationsSent.Contains(text))
			{
				continue;
			}
			if (Vector3.Distance(origin, key.Position) > VicinityDetectDistance)
			{
				continue;
			}
			BotBossPatch.vicinityNotificationsSent.Add(text);
			bool flag = BossNotifierPlugin.pluralBosses.Contains(text) || BossNotifierPlugin.customBossGroups.Contains(text);
			BotBossPatch.vicinityNotifications.Enqueue(text + " " + (flag ? "have" : "has") + " been detected in your vicinity.");
			BossNotifierPlugin.Log((LogLevel)16, $"{text} detected in vicinity ({VicinityDetectDistance}m) on Fika client");
		}
	}

	private void UpdateAllMarkers()
	{
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_0101: Unknown result type (might be due to invalid IL or missing references)
		//IL_0106: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_010c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0111: Unknown result type (might be due to invalid IL or missing references)
		//IL_011a: Unknown result type (might be due to invalid IL or missing references)
		//IL_011d: Unknown result type (might be due to invalid IL or missing references)
		if ((Object)(object)_mainCamera == (Object)null)
		{
			_mainCamera = Camera.main;
			if ((Object)(object)_mainCamera == (Object)null)
			{
				return;
			}
		}
		Vector3 position = ((Component)_mainCamera).transform.position;
		List<Player> list = new List<Player>();
		RaycastHit val2 = default;
		foreach (KeyValuePair<Player, BossMarkerInfo> bossMarker in _bossMarkers)
		{
			Player key = bossMarker.Key;
			BossMarkerInfo value = bossMarker.Value;
			if ((Object)(object)key == (Object)null || (Object)(object)value.MarkerObject == (Object)null)
			{
				list.Add(key);
				continue;
			}
			if (key.HealthController != null && !key.HealthController.IsAlive)
			{
				list.Add(key);
				continue;
			}
			float num = Vector3.Distance(position, key.Position);
			if (num > (float)BossNotifierPlugin.visibilityDistance.Value)
			{
				value.MarkerObject.SetActive(false);
				continue;
			}
			if (!BossNotifierPlugin.showThroughWalls.Value)
			{
				Vector3 val = key.Position + Vector3.up * 1.5f - position;
				int num2 = 71680;
				if (Physics.Raycast(position, val.normalized, out val2, num, num2) && val2.distance < num - 1f)
				{
					value.MarkerObject.SetActive(false);
					continue;
				}
			}
			value.MarkerObject.SetActive(true);
			UpdateMarkerPosition(value.MarkerObject, key);
			UpdateMarkerScale(value.MarkerObject, num);
			UpdateMarkerText(value, num);
		}
		foreach (Player item in list)
		{
			if (_bossMarkers.TryGetValue(item, out var value2))
			{
				if ((Object)(object)value2.MarkerObject != (Object)null)
				{
					Object.Destroy((Object)(object)value2.MarkerObject);
				}
				_bossMarkers.Remove(item);
			}
		}
	}

	private void UpdateMarkerPosition(GameObject marker, Player player)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		if (!((Object)(object)player == (Object)null) && !((Object)(object)marker == (Object)null))
		{
			Vector3 position = player.Position + Vector3.up * 2.2f;
			marker.transform.position = position;
		}
	}

	private void UpdateMarkerScale(GameObject marker, float distance)
	{
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		float value = BossNotifierPlugin.markerBaseScale.Value;
		float value2 = BossNotifierPlugin.markerMaxScale.Value;
		float num = value * (1f + distance / 50f);
		num = Mathf.Clamp(num, value, value2);
		marker.transform.localScale = Vector3.one * num;
	}

	private void UpdateMarkerText(BossMarkerInfo markerInfo, float distance)
	{
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0130: Unknown result type (might be due to invalid IL or missing references)
		string markerCharacter = BossNotifierPlugin.GetMarkerCharacter(markerInfo.GroupName);
		Color markerColor = BossNotifierPlugin.GetMarkerColor(markerInfo.GroupName);
		if ((Object)(object)markerInfo.SymbolTextMesh != (Object)null)
		{
			markerInfo.SymbolTextMesh.text = markerCharacter;
			markerInfo.SymbolTextMesh.fontSize = BossNotifierPlugin.fontSize.Value;
			markerInfo.SymbolTextMesh.color = markerColor;
		}
		if ((Object)(object)markerInfo.InfoTextMesh != (Object)null)
		{
			bool value = BossNotifierPlugin.showBossName.Value;
			bool value2 = BossNotifierPlugin.showDistance.Value;
			if (value & value2)
			{
				markerInfo.InfoTextMesh.text = $"{markerInfo.BossName}\n{distance:F0}m";
			}
			else if (value && !value2)
			{
				markerInfo.InfoTextMesh.text = markerInfo.BossName;
			}
			else if (!value & value2)
			{
				markerInfo.InfoTextMesh.text = $"{distance:F0}m";
			}
			else
			{
				markerInfo.InfoTextMesh.text = "";
			}
			markerInfo.InfoTextMesh.color = markerColor;
			float num = (float)BossNotifierPlugin.fontSize.Value / 64f;
			float num2 = 0.05f * num;
			((Component)markerInfo.InfoTextMesh).transform.localPosition = new Vector3(0f, num2, 0f);
		}
	}

	private void ToggleAllMarkers(bool visible)
	{
		foreach (BossMarkerInfo value in _bossMarkers.Values)
		{
			if ((Object)(object)value.MarkerObject != (Object)null)
			{
				value.MarkerObject.SetActive(visible);
			}
		}
	}

	/// <summary>
	/// bot 出生包到货时立刻把播报提前到 0.5 秒后（同一波 bot 会不断刷新这个计时器，
	/// 等价于“这一波全部登记完再播”），而不是等满 <see cref="BossDataWaitSeconds"/>。
	/// </summary>
	private void NotifyBotDataArrived()
	{
		if (_raidStartAnnounced || _announceScheduled || !_uiReady)
		{
			return;
		}
		_announceScheduled = true;
		((MonoBehaviour)this).CancelInvoke("SendBossNotifications");
		((MonoBehaviour)this).Invoke("SendBossNotifications", 0.5f);
		BossNotifierPlugin.Log((LogLevel)16, $"Bot data arrived ({_botObservations} observation(s)) - announcing boss list in 0.5s");
	}

	/// <summary>
	/// 进图后播一条自检提示，用来确认 EFT 的通知通道在本进程里确实可用。
	/// 看不到这条就说明问题不在 boss 数据，而在通知显示链路。
	/// </summary>
	private void SendSelfTestNotification()
	{
		try
		{
			string text = "BossNotifier: online";
			NotificationManager.DisplayMessageNotification(text, (ENotificationDurationType)1, (ENotificationIconType)0, (Color?)null);
			BossNotifierPlugin.Log((LogLevel)16, "Self-test notification sent: " + text);
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)2, "Self-test notification failed: " + ex);
		}
	}

	private void SendBossNotifications()
	{
		if (!ShouldFunction() || intelCenterLevel < BossNotifierPlugin.intelCenterUnlockLevel.Value)
		{
			BossNotifierPlugin.Log((LogLevel)16, $"SendBossNotifications skipped (shouldFunction={ShouldFunction()}, intelCenterLevel={intelCenterLevel}, required={BossNotifierPlugin.intelCenterUnlockLevel.Value})");
			return;
		}
		try
		{
			GenerateBossNotifications();
			if (bossNotificationMessages == null || bossNotificationMessages.Count == 0)
			{
				// 还在等 bot 出生包（一个 AI 都没看到）才值得重试；
				// 已经看到 AI 了说明数据到货，直接定论 “No Bosses Located”。
				if (!FikaCompat.IsHost && _botObservations == 0 && Time.realtimeSinceStartup - _raidStartTime < BossDataWaitSeconds)
				{
					((MonoBehaviour)this).Invoke("SendBossNotifications", BossDataRetryInterval);
					BossNotifierPlugin.Log((LogLevel)16, $"No bot data yet - retrying boss notification in {BossDataRetryInterval:0.#}s");
					return;
				}
				BossNotifierPlugin.Log((LogLevel)16, $"Sending notification: No Bosses Located (bots observed: {_botObservations})");
				NotificationManager.DisplayMessageNotification("No Bosses Located", (ENotificationDurationType)1, (ENotificationIconType)0, (Color?)null);
				_raidStartAnnounced = true;
				_announceScheduled = false;
				return;
			}
			BossNotifierPlugin.Log((LogLevel)16, $"Sending {bossNotificationMessages.Count} boss notification(s): {string.Join(" | ", bossNotificationMessages.ToArray())}");
			foreach (string bossNotificationMessage in bossNotificationMessages)
			{
				NotificationManager.DisplayMessageNotification(bossNotificationMessage, (ENotificationDurationType)1, (ENotificationIconType)0, (Color?)null);
			}
			_raidStartAnnounced = true;
			_announceScheduled = false;
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log((LogLevel)2, "SendBossNotifications failed: " + ex);
		}
	}

	public void GenerateBossNotifications()
	{
		try
		{
			GenerateBossNotificationsInternal();
		}
		catch (Exception ex)
		{
			bossNotificationMessages = new List<string>();
			BossNotifierPlugin.Log((LogLevel)2, "GenerateBossNotifications failed: " + ex);
		}
	}

	private void GenerateBossNotificationsInternal()
	{
		bossNotificationMessages = new List<string>();
		bool flag = IsDay();
		bool flag2 = intelCenterLevel >= BossNotifierPlugin.intelCenterLocationUnlockLevel.Value;
		bool flag3 = intelCenterLevel >= BossNotifierPlugin.intelCenterDetectedUnlockLevel.Value;
		bool flag4 = false;
		string text = "";
		bool flag5 = false;
		foreach (KeyValuePair<string, string> item3 in BossLocationSpawnPatch.bossesInRaid)
		{
			if (flag && item3.Key.Equals("Cultists"))
			{
				continue;
			}
			if (BossNotifierPlugin.goonMembers.Contains(item3.Key))
			{
				if (!flag4)
				{
					text = item3.Value;
					flag5 = BotBossPatch.spawnedBosses.Contains("Knight") || BotBossPatch.spawnedBosses.Contains("Big Pipe") || BotBossPatch.spawnedBosses.Contains("Birdeye");
					string item;
					if (BotBossPatch.deadBosses.Contains("Knight") && BotBossPatch.deadBosses.Contains("Big Pipe") && BotBossPatch.deadBosses.Contains("Birdeye"))
					{
						item = "Goons have been eliminated. ☠";
					}
					else
					{
						item = ((flag2 && text != null && !text.Equals("")) ? ("Goons have been located near " + text + "." + ((flag3 & flag5) ? " ✓" : "")) : ("Goons have been located." + ((flag3 & flag5) ? " ✓" : "")));
					}
					bossNotificationMessages.Add(item);
					flag4 = true;
				}
				continue;
			}
			bool flag6 = BotBossPatch.spawnedBosses.Contains(item3.Key);
			bool flag7 = ((!BossNotifierPlugin.pluralBosses.Contains(item3.Key) && !BossNotifierPlugin.customBossGroups.Contains(item3.Key)) ? BotBossPatch.deadBosses.Contains(item3.Key) : BossNotifierPlugin.IsGroupFullyEliminated(item3.Key));
			string item2;
			if (flag7)
			{
				item2 = item3.Key + " " + (BossNotifierPlugin.pluralBosses.Contains(item3.Key) ? "have" : "has") + " been eliminated. ☠";
			}
			else
			{
				item2 = ((flag2 && item3.Value != null && !item3.Value.Equals("")) ? (item3.Key + " " + (BossNotifierPlugin.pluralBosses.Contains(item3.Key) ? "have" : "has") + " been located near " + item3.Value + "." + ((flag3 & flag6) ? " ✓" : "")) : (item3.Key + " " + (BossNotifierPlugin.pluralBosses.Contains(item3.Key) ? "have" : "has") + " been located." + ((flag3 & flag6) ? " ✓" : "")));
			}
			bossNotificationMessages.Add(item2);
		}
	}

	private bool IsDay()
	{
		try
		{
			GameWorld instance = Singleton<GameWorld>.Instance;
			if ((Object)(object)instance != (Object)null && instance.GameDateTime != null)
			{
				int hour = instance.GameDateTime.Calculate().Hour;
				if (hour >= 7)
				{
					return hour < 22;
				}
				return false;
			}
		}
		catch (Exception ex)
		{
			// Fika 客户端上 GameDateTime 可能还没同步，取不到时间就按白天处理。
			BossNotifierPlugin.Log((LogLevel)4, "IsDay failed, assuming daytime: " + ex.Message);
		}
		return true;
	}

	public void OnDestroy()
	{
		//IL_0074: Unknown result type (might be due to invalid IL or missing references)
		//IL_007e: Expected Obj, but got Unknown
		if ((Object)(object)_gameWorld != (Object)null)
		{
			_gameWorld.OnPersonAdd -= OnPersonAdd;
		}
		foreach (BossMarkerInfo value in _bossMarkers.Values)
		{
			if ((Object)(object)value.MarkerObject != (Object)null)
			{
				Object.Destroy((Object)(object)value.MarkerObject);
			}
			if ((Object)(object)value.Player != (Object)null)
			{
				value.Player.OnPlayerDeadOrUnspawn -= OnBossDeadOrUnspawn;
			}
		}
		_bossMarkers.Clear();
		BossLocationSpawnPatch.bossesInRaid.Clear();
		BotBossPatch.spawnedBosses.Clear();
		BotBossPatch.deadBosses.Clear();
		BotBossPatch.vicinityNotificationsSent.Clear();
		BossNotifierPlugin.groupSeenCount.Clear();
		BossNotifierPlugin.groupDeadCount.Clear();
		_announcedGroups.Clear();
		_raidStartAnnounced = false;
		_botObservations = 0;
		_announceScheduled = false;
		BossZoneLocator.Reset();
		BossNotifierPlugin.InvokeOnRaidEnded();
	}

	/// <summary>
	/// 本进程是否应该显示 boss 提示。
	///
	/// 原版写法是 <c>FikaBackendUtils.ClientType == 2</c>，而 Fika 的枚举是
	/// None = 0 / Client = 1 / Host = 2，于是只有房主会显示提示；
	/// 加入别人房间的客户端（ClientType = 1）被整体关掉——
	/// Fika 无头主机没有本地玩家，能显示提示的恰恰就是这些客户端，所以提示全没了。
	/// 现在的判定：只有无头主机（没有 HUD）不显示，其它情况（单机 / 客户端 / 有画面的房主）都显示。
	/// </summary>
	public bool ShouldFunction()
	{
		return FikaCompat.CanRenderUi;
	}

	private bool IsKeyPressed(KeyboardShortcut key)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0030: Unknown result type (might be due to invalid IL or missing references)
		if (!UnityInput.Current.GetKeyDown(key.MainKey))
		{
			return false;
		}
		foreach (KeyCode modifier in key.Modifiers)
		{
			if (!UnityInput.Current.GetKey(modifier))
			{
				return false;
			}
		}
		return true;
	}
}
