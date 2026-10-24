using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using Newtonsoft.Json;
using SPT.Reflection.Patching;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BossNotifier;

[BepInPlugin("Mattexe.BossNotifier", "BossNotifier", "1.2.1.5")]
[BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
public class BossNotifierPlugin : BaseUnityPlugin
{
	public static PropertyInfo FikaClientTypeProperty;

	public static ConfigEntry<KeyboardShortcut> showBossesKeyCode;

	public static ConfigEntry<bool> showNotificationsOnRaidStart;

	public static ConfigEntry<int> intelCenterUnlockLevel;

	public static ConfigEntry<int> intelCenterLocationUnlockLevel;

	public static ConfigEntry<int> intelCenterDetectedUnlockLevel;

	public static ConfigEntry<bool> enableMarkers;

	public static ConfigEntry<KeyboardShortcut> toggleMarkersKey;

	public static ConfigEntry<bool> showThroughWalls;

	public static ConfigEntry<string> markerCharacter;

	public static ConfigEntry<bool> showBossName;

	public static ConfigEntry<bool> showDistance;

	public static ConfigEntry<int> fontSize;

	public static ConfigEntry<float> markerBaseScale;

	public static ConfigEntry<float> markerMaxScale;

	public static ConfigEntry<int> visibilityDistance;

	public static ConfigEntry<Color> markerColor;

	private static ManualLogSource logger;

	public static readonly Dictionary<WildSpawnType, string> bossNames = new Dictionary<WildSpawnType, string>
	{
		{
			(WildSpawnType)3,
			"Reshala"
		},
		{
			(WildSpawnType)26,
			"Knight"
		},
		{
			(WildSpawnType)27,
			"Big Pipe"
		},
		{
			(WildSpawnType)28,
			"Birdeye"
		},
		{
			(WildSpawnType)21,
			"Cultists"
		},
		{
			(WildSpawnType)22,
			"Tagilla"
		},
		{
			(WildSpawnType)6,
			"Killa"
		},
		{
			(WildSpawnType)29,
			"Zryachiy"
		},
		{
			(WildSpawnType)11,
			"Glukhar"
		},
		{
			(WildSpawnType)17,
			"Sanitar"
		},
		{
			(WildSpawnType)7,
			"Shturman"
		},
		{
			(WildSpawnType)32,
			"Kaban"
		},
		{
			(WildSpawnType)25,
			"Santa Claus"
		},
		{
			(WildSpawnType)35,
			"Blood Hounds"
		},
		{
			(WildSpawnType)37,
			"Crazy Scavs"
		},
		{
			(WildSpawnType)24,
			"Rogues"
		},
		{
			(WildSpawnType)43,
			"Kollontay"
		},
		{
			(WildSpawnType)47,
			"Partisan"
		},
		{
			(WildSpawnType)4206927,
			"Punisher"
		},
		{
			(WildSpawnType)199,
			"Legion"
		}
	};

	public static CustomBossConfig customBossConfig;

	public static Dictionary<int, string> customBossLookup = new Dictionary<int, string>();

	public static Dictionary<int, string> customBossDisplayNames = new Dictionary<int, string>();

	public static HashSet<string> customBossGroups = new HashSet<string>();

	public static Dictionary<string, ConfigEntry<string>> customGroupMarkerCharacters = new Dictionary<string, ConfigEntry<string>>();

	public static Dictionary<string, ConfigEntry<Color>> customGroupMarkerColors = new Dictionary<string, ConfigEntry<Color>>();

	public static HashSet<string> loadedCustomMods = new HashSet<string>();

	public static Dictionary<string, int> groupSeenCount = new Dictionary<string, int>();

	public static Dictionary<string, int> groupDeadCount = new Dictionary<string, int>();

	public static HashSet<string> pluralBosses = new HashSet<string> { "Goons", "Cultists", "Blood Hounds", "Crazy Scavs", "Rogues" };

	public static readonly HashSet<string> goonMembers = new HashSet<string> { "Knight", "Big Pipe", "Birdeye" };

	public static readonly Dictionary<string, string> zoneNames = new Dictionary<string, string>
	{
		{ "ZoneBrige", "Bridge" },
		{ "ZoneCrossRoad", "Crossroads" },
		{ "ZoneDormitory", "Dormitory" },
		{ "ZoneGasStation", "Gas Station" },
		{ "ZoneFactoryCenter", "Factory Center" },
		{ "ZoneFactorySide", "Factory Side" },
		{ "ZoneOldAZS", "Old Gas Station" },
		{ "ZoneBlockPost", "Checkpoint" },
		{ "ZoneTankSquare", "Old Construction" },
		{ "ZoneWade", "RUAF Roadblock" },
		{ "ZoneCustoms", "Customs" },
		{ "ZoneScavBase", "Scav Base" },
		{ "BotZone", "" },
		{ "ZoneCenterBot", "Center Floor 2" },
		{ "ZoneCenter", "Center Floor 1" },
		{ "ZoneOLI", "OLI" },
		{ "ZoneIDEA", "IDEA" },
		{ "ZoneGoshan", "Goshan" },
		{ "ZoneIDEAPark", "IDEA Parking" },
		{ "ZoneOLIPark", "OLI Parking" },
		{ "ZoneTrucks", "Trucks" },
		{ "ZoneRoad", "Road" },
		{ "ZonePowerStation", "Power Station" },
		{ "BotZoneFloor1", "Floor 1" },
		{ "BotZoneFloor2", "Floor 2" },
		{ "BotZoneBasement", "Basement" },
		{ "BotZoneGate1", "Gate 1" },
		{ "BotZoneGate2", "Gate 2" },
		{ "Zone_Containers", "Containers" },
		{ "Zone_Rocks", "Rocks" },
		{ "Zone_Chalet", "Chalet" },
		{ "Zone_Village", "Village" },
		{ "Zone_Bridge", "Bridge" },
		{ "Zone_OldHouse", "Old House" },
		{ "Zone_LongRoad", "Long Road" },
		{ "Zone_RoofBeach", "Roof Beach" },
		{ "Zone_DestroyedHouse", "Destroyed House" },
		{ "Zone_RoofContainers", "Roof Containers" },
		{ "Zone_Blockpost", "Checkpoint" },
		{ "Zone_RoofRocks", "Roof Rocks" },
		{ "Zone_TreatmentRocks", "Treatment Rocks" },
		{ "Zone_TreatmentContainers", "Treatment Containers" },
		{ "Zone_TreatmentBeach", "Treatment Beach" },
		{ "Zone_Hellicopter", "Helicopter" },
		{ "Zone_SniperPeak", "Sniper Peak" },
		{ "Zone_Island", "Island" },
		{ "ZoneRailStrorage", "Rail Storage" },
		{ "ZonePTOR1", "Black Pawn" },
		{ "ZonePTOR2", "White Knight" },
		{ "ZoneBarrack", "Barracks" },
		{ "ZoneBunkerStorage", "Bunker Storage" },
		{ "ZoneSubStorage", "Sub Storage" },
		{ "ZoneSubCommand", "Sub Command" },
		{ "ZoneSandbox", "" },
		{ "ZoneGreenHouses", "Green Houses" },
		{ "ZoneIsland", "Island" },
		{ "ZoneForestGasStation", "Forest Gas Station" },
		{ "ZoneBunker", "Bunker" },
		{ "ZoneBusStation", "Bus Station" },
		{ "ZonePort", "Pier" },
		{ "ZoneForestTruck", "Forest Truck" },
		{ "ZoneForestSpawn", "Forest" },
		{ "ZoneSanatorium1", "Sanatorium West" },
		{ "ZoneSanatorium2", "Sanatorium East" },
		{ "ZoneStartVillage", "Village" },
		{ "ZoneMeteoStation", "Weather Station" },
		{ "ZoneRailWays", "Railways" },
		{ "ZoneSmuglers", "Smugglers" },
		{ "ZonePassClose", "Pass" },
		{ "ZoneTunnel", "Tunnel" },
		{ "ZoneSW01", "SW01" },
		{ "ZoneConstruction", "Construction" },
		{ "ZoneCarShowroom", "Car Showroom" },
		{ "ZoneCinema", "Cinema" },
		{ "ZoneFactory", "Factory" },
		{ "ZoneHotel_1", "Hotel 1" },
		{ "ZoneHotel_2", "Hotel 2" },
		{ "ZoneConcordia_1", "Concordia" },
		{ "ZoneConcordiaParking", "Concordia Parking" },
		{ "ZoneColumn", "Column" },
		{ "ZoneSW00", "SW00" },
		{ "ZoneStilo", "Stilo" },
		{ "ZoneCard1", "Cardinal" },
		{ "ZoneMvd", "MVD" },
		{ "ZoneClimova", "Klimova" },
		{ "ZoneWoodCutter", "Wood Cutter" },
		{ "ZoneHouse", "House" },
		{ "ZoneBigRocks", "Big Rocks" },
		{ "ZoneHighRocks", "High Rocks" },
		{ "ZoneMiniHouse", "Mini House" },
		{ "ZoneRedHouse", "Red House" },
		{ "ZoneScavBase2", "Scav Base" },
		{ "ZoneClearVill", "Village" },
		{ "ZoneBrokenVill", "Broken Village" },
		{ "ZoneUsecBase", "USEC Base" },
		{ "ZoneStoneBunker", "Stone Bunker" },
		{ "ZoneDepo", "Depot" }
	};

	private static readonly Dictionary<string, Color> defaultGroupColors = new Dictionary<string, Color>
	{
		{
			"RUAF",
			new Color(0.2f, 0.6f, 0.2f, 1f)
		},
		{
			"UNTAR",
			new Color(0.3f, 0.5f, 1f, 1f)
		},
		{
			"Black Division",
			new Color(0.1f, 0.1f, 0.1f, 1f)
		}
	};

	public static event Action OnPluginAwake;

	public static event Action OnRaidStarted;

	public static event Action<string> OnBossDied;

	public static event Action OnRaidEnded;

	public static void InvokeOnRaidStarted()
	{
		OnRaidStarted?.Invoke();
	}

	public static void InvokeOnBossDied(string bossName)
	{
		OnBossDied?.Invoke(bossName);
	}

	public static void InvokeOnRaidEnded()
	{
		OnRaidEnded?.Invoke();
	}

	public static void Log(LogLevel level, string msg)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		logger.Log(level, (object)msg);
	}

	public static bool IsGroupFullyEliminated(string groupName)
	{
		if (!groupSeenCount.TryGetValue(groupName, out var value) || value == 0)
		{
			return false;
		}
		groupDeadCount.TryGetValue(groupName, out var value2);
		return value2 >= value;
	}

	private void Awake()
	{
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_009d: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a7: Expected Obj, but got Unknown
		//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d8: Expected Obj, but got Unknown
		//IL_00ff: Unknown result type (might be due to invalid IL or missing references)
		//IL_0109: Expected Obj, but got Unknown
		//IL_0145: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_01f6: Expected Obj, but got Unknown
		//IL_0263: Unknown result type (might be due to invalid IL or missing references)
		//IL_026d: Expected Obj, but got Unknown
		//IL_02a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_02aa: Expected Obj, but got Unknown
		//IL_02dd: Unknown result type (might be due to invalid IL or missing references)
		//IL_02e7: Expected Obj, but got Unknown
		//IL_0317: Unknown result type (might be due to invalid IL or missing references)
		//IL_0321: Expected Obj, but got Unknown
		//IL_034a: Unknown result type (might be due to invalid IL or missing references)
		logger = Logger;
		FikaCompat.Resolve();
		showBossesKeyCode = ((BaseUnityPlugin)this).Config.Bind<KeyboardShortcut>("1. General", "Keyboard Shortcut", new KeyboardShortcut((KeyCode)111, Array.Empty<KeyCode>()), "Key to show boss notifications.");
		showNotificationsOnRaidStart = ((BaseUnityPlugin)this).Config.Bind<bool>("1. General", "Show Bosses on Raid Start", true, "Show boss notifications on raid start.");
		intelCenterUnlockLevel = ((BaseUnityPlugin)this).Config.Bind<int>("2. Intel Center Unlocks (4 means Disabled)", "1. Intel Center Level Requirement", 0, new ConfigDescription("Level to unlock plain notifications at.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 4), Array.Empty<object>()));
		intelCenterLocationUnlockLevel = ((BaseUnityPlugin)this).Config.Bind<int>("2. Intel Center Unlocks (4 means Disabled)", "2. Intel Center Location Level Requirement", 0, new ConfigDescription("Unlocks showing boss spawn location in notification.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 4), Array.Empty<object>()));
		intelCenterDetectedUnlockLevel = ((BaseUnityPlugin)this).Config.Bind<int>("2. Intel Center Unlocks (4 means Disabled)", "3. Intel Center Detection Requirement", 0, new ConfigDescription("Unlocks showing boss detected notification. (When you get near a boss)", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 4), Array.Empty<object>()));
		enableMarkers = ((BaseUnityPlugin)this).Config.Bind<bool>("3. Boss Markers", "1. Enable Markers", true, "Show 3D markers above boss heads.");
		toggleMarkersKey = ((BaseUnityPlugin)this).Config.Bind<KeyboardShortcut>("3. Boss Markers", "2. Toggle Markers Key", new KeyboardShortcut((KeyCode)112, Array.Empty<KeyCode>()), "Key to toggle marker visibility.");
		showThroughWalls = ((BaseUnityPlugin)this).Config.Bind<bool>("3. Boss Markers", "3. Show Through Walls", true, "If enabled, markers are visible through walls. If disabled, markers only show when you have line of sight to the boss.");
		markerCharacter = ((BaseUnityPlugin)this).Config.Bind<string>("3. Boss Markers", "4. Marker Character", "▼", new ConfigDescription("Character to display as marker.", (AcceptableValueBase)(object)new AcceptableValueList<string>(new string[9] { "▼", "▽", "↓", "●", "◆", "★", "☠", "◉", "▲" }), Array.Empty<object>()));
		showBossName = ((BaseUnityPlugin)this).Config.Bind<bool>("3. Boss Markers", "5. Show Boss Name", true, "Show boss name on marker.");
		showDistance = ((BaseUnityPlugin)this).Config.Bind<bool>("3. Boss Markers", "6. Show Distance", true, "Show distance to boss on marker.");
		fontSize = ((BaseUnityPlugin)this).Config.Bind<int>("3. Boss Markers", "7. Marker Size", 64, new ConfigDescription("Font size of the marker.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(32, 128), Array.Empty<object>()));
		markerBaseScale = ((BaseUnityPlugin)this).Config.Bind<float>("3. Boss Markers", "8. Base Scale", 0.04f, new ConfigDescription("Base scale of markers.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.01f, 0.2f), Array.Empty<object>()));
		markerMaxScale = ((BaseUnityPlugin)this).Config.Bind<float>("3. Boss Markers", "9. Max Scale", 0.3f, new ConfigDescription("Maximum scale at far distances.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.1f, 1f), Array.Empty<object>()));
		visibilityDistance = ((BaseUnityPlugin)this).Config.Bind<int>("3. Boss Markers", "10. Visibility Distance", 200, new ConfigDescription("Maximum distance (meters) to show markers.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(50, 500), Array.Empty<object>()));
		markerColor = ((BaseUnityPlugin)this).Config.Bind<Color>("3. Boss Markers", "11. Marker Color", new Color(1f, 0f, 0f, 1f), "Color of boss markers (red by default).");
		LoadCustomBossConfig();
		((ModulePatch)new BossLocationSpawnPatch()).Enable();
		((ModulePatch)new NewGamePatch()).Enable();
		((ModulePatch)new BotBossPatch()).Enable();
		((BaseUnityPlugin)this).Config.SettingChanged += Config_SettingChanged;
		Logger.LogInfo((object)"Plugin BossNotifier v1.2.1 is loaded!");
		OnPluginAwake?.Invoke();
	}

	private void Config_SettingChanged(object sender, SettingChangedEventArgs e)
	{
		_ = e.ChangedSetting;
		if (BossNotifierMono.Instance != null)
		{
			BossNotifierMono.Instance.GenerateBossNotifications();
		}
	}

	public static string GetBossName(WildSpawnType type)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected I4, but got Unknown
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if (bossNames.ContainsKey(type))
		{
			return bossNames[type];
		}
		int key = (int)type;
		if (customBossLookup.ContainsKey(key))
		{
			return customBossLookup[key];
		}
		return null;
	}

	public static string GetBossDisplayName(WildSpawnType type)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected I4, but got Unknown
		//IL_0012: Unknown result type (might be due to invalid IL or missing references)
		if (bossNames.ContainsKey(type))
		{
			return bossNames[type];
		}
		int key = (int)type;
		if (customBossDisplayNames.ContainsKey(key))
		{
			return customBossDisplayNames[key];
		}
		return null;
	}

	public static string GetMarkerCharacter(string groupName)
	{
		if (customGroupMarkerCharacters.ContainsKey(groupName))
		{
			return customGroupMarkerCharacters[groupName].Value;
		}
		return markerCharacter.Value;
	}

	public static Color GetMarkerColor(string groupName)
	{
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		if (customGroupMarkerColors.ContainsKey(groupName))
		{
			return customGroupMarkerColors[groupName].Value;
		}
		return markerColor.Value;
	}

	public static bool IsBoss(WildSpawnType type)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		//IL_000f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected I4, but got Unknown
		if (bossNames.ContainsKey(type))
		{
			return true;
		}
		int key = (int)type;
		return customBossLookup.ContainsKey(key);
	}

	public static string GetZoneName(string zoneId)
	{
		if (zoneNames.ContainsKey(zoneId))
		{
			return zoneNames[zoneId];
		}
		string text = zoneId.Replace("Bot", "").Replace("Zone", "");
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (char.IsUpper(c) && i != 0 && i < text.Length - 1 && !char.IsUpper(text[i + 1]) && !char.IsDigit(text[i + 1]))
			{
				stringBuilder.Append(" ");
			}
			stringBuilder.Append(c);
		}
		return stringBuilder.ToString().Replace("_", " ").Trim();
	}

	private bool IsPluginLoaded(string pluginGUID)
	{
		if (string.IsNullOrEmpty(pluginGUID))
		{
			return true;
		}
		return Chainloader.PluginInfos.ContainsKey(pluginGUID);
	}

	private void LoadCustomBossConfig()
	{
		//IL_01f9: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e6: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Unknown result type (might be due to invalid IL or missing references)
		//IL_028d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0297: Expected Obj, but got Unknown
		//IL_02b5: Unknown result type (might be due to invalid IL or missing references)
		string text = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "custom_bosses.json");
		try
		{
			if (File.Exists(text))
			{
				customBossConfig = JsonConvert.DeserializeObject<CustomBossConfig>(File.ReadAllText(text));
				if (customBossConfig?.customBossGroups == null)
				{
					return;
				}
				int num = 0;
				int num2 = 4;
				foreach (CustomBossGroup customBossGroup in customBossConfig.customBossGroups)
				{
					if (!IsPluginLoaded(customBossGroup.pluginGUID))
					{
						Logger.LogInfo((object)("Skipping " + customBossGroup.groupName + " - mod not installed (GUID: " + customBossGroup.pluginGUID + ")"));
						continue;
					}
					Logger.LogInfo((object)("Mod detected: " + customBossGroup.groupName + " (GUID: " + customBossGroup.pluginGUID + ")"));
					loadedCustomMods.Add(customBossGroup.groupName);
					foreach (CustomBossMember member in customBossGroup.members)
					{
						customBossLookup[member.id] = customBossGroup.groupName;
						customBossDisplayNames[member.id] = member.displayName;
					}
					customBossGroups.Add(customBossGroup.groupName);
					if (customBossGroup.isPlural)
					{
						pluralBosses.Add(customBossGroup.groupName);
					}
					string text2 = $"{num2}. {customBossGroup.groupName} Markers";
					Color val = (Color)(defaultGroupColors.ContainsKey(customBossGroup.groupName) ? defaultGroupColors[customBossGroup.groupName] : new Color(1f, 0.5f, 0f, 1f));
					customGroupMarkerCharacters[customBossGroup.groupName] = ((BaseUnityPlugin)this).Config.Bind<string>(text2, "Marker Character", "▼", new ConfigDescription("Character to display as marker for " + customBossGroup.groupName + ".", (AcceptableValueBase)(object)new AcceptableValueList<string>(new string[9] { "▼", "▽", "↓", "●", "◆", "★", "☠", "◉", "▲" }), Array.Empty<object>()));
					customGroupMarkerColors[customBossGroup.groupName] = ((BaseUnityPlugin)this).Config.Bind<Color>(text2, "Marker Color", val, "Color of " + customBossGroup.groupName + " markers.");
					num++;
					num2++;
				}
				Logger.LogInfo((object)$"Loaded {num} custom boss groups with {customBossLookup.Count} total IDs");
			}
			else
			{
				Logger.LogWarning((object)("Custom boss config not found at: " + text));
			}
		}
		catch (Exception ex)
		{
			Logger.LogError((object)("Failed to load custom boss config: " + ex.Message));
		}
	}

	static BossNotifierPlugin()
	{
		//IL_080e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0832: Unknown result type (might be due to invalid IL or missing references)
		//IL_0856: Unknown result type (might be due to invalid IL or missing references)
	}
}
