using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using SPT.Reflection.Patching;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BossNotifier;

public class BotBossPatch : ModulePatch
{
	public static HashSet<string> spawnedBosses = new HashSet<string>();

	public static HashSet<string> deadBosses = new HashSet<string>();

	public static HashSet<string> vicinityNotificationsSent = new HashSet<string>();

	public static Queue<string> vicinityNotifications = new Queue<string>();

	protected override MethodBase GetTargetMethod()
	{
		return typeof(BotBoss).GetConstructors()[0];
	}

	[PatchPostfix]
	private static void PatchPostfix(BotBoss __instance)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_004d: Unknown result type (might be due to invalid IL or missing references)
		string bossName = BossNotifierPlugin.GetBossName(__instance.Owner.Profile.Info.Settings.Role);
		if (bossName == null)
		{
			return;
		}
		Vector3 position = __instance.Player().Position;
		string text = $"{(int)position.x}, {(int)position.y}, {(int)position.z}";
		BossNotifierPlugin.Log((LogLevel)16, bossName + " has spawned at " + text + " on " + Singleton<GameWorld>.Instance.LocationId);
		spawnedBosses.Add(bossName);
		if (!BossLocationSpawnPatch.bossesInRaid.ContainsKey(bossName))
		{
			BossLocationSpawnPatch.bossesInRaid[bossName] = "";
			BossNotifierPlugin.Log((LogLevel)16, "Registered " + bossName + " in bossesInRaid via spawn fallback");
			if ((Object)(object)BossNotifierMono.Instance != (Object)null)
			{
				BossNotifierMono.Instance.GenerateBossNotifications();
			}
		}
		string text2 = (BossNotifierPlugin.goonMembers.Contains(bossName) ? "Goons" : bossName);
		if (!vicinityNotificationsSent.Contains(text2))
		{
			vicinityNotificationsSent.Add(text2);
			bool flag = BossNotifierPlugin.pluralBosses.Contains(text2);
			vicinityNotifications.Enqueue(text2 + " " + (flag ? "have" : "has") + " been detected in your vicinity.");
		}
	}
}
