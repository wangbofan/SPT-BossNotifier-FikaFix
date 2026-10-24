using System.Collections.Generic;
using System.Reflection;
using BepInEx.Logging;
using SPT.Reflection.Patching;

namespace BossNotifier;

public class BossLocationSpawnPatch : ModulePatch
{
	public static Dictionary<string, string> bossesInRaid = new Dictionary<string, string>();

	protected override MethodBase GetTargetMethod()
	{
		return typeof(BossLocationSpawn).GetMethod("Init");
	}

	private static void TryAddBoss(string boss, string location)
	{
		if (location == null)
		{
			ModulePatch.Logger.LogError((object)"Tried to add boss with null location.");
		}
		else if (bossesInRaid.ContainsKey(boss))
		{
			if (!bossesInRaid[boss].Contains(location) && !location.Equals(""))
			{
				if (bossesInRaid[boss].Equals(""))
				{
					bossesInRaid[boss] = location;
					return;
				}
				Dictionary<string, string> dictionary = bossesInRaid;
				dictionary[boss] = dictionary[boss] + ", " + location;
			}
		}
		else
		{
			bossesInRaid.Add(boss, location);
		}
	}

	[PatchPostfix]
	private static void PatchPostfix(BossLocationSpawn __instance)
	{
		//IL_0011: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Expected I4, but got Unknown
		//IL_001f: Unknown result type (might be due to invalid IL or missing references)
		if (!__instance.ShallSpawn || BossNotifierPlugin.customBossLookup.ContainsKey((int)__instance.BossType))
		{
			return;
		}
		string bossName = BossNotifierPlugin.GetBossName(__instance.BossType);
		if (bossName != null)
		{
			string zoneName = BossNotifierPlugin.GetZoneName(__instance.BornZone);
			BossNotifierPlugin.Log((LogLevel)16, "Boss " + bossName + " @ zone " + __instance.BornZone + " translated to " + ((zoneName == null) ? __instance.BornZone.Replace("Bot", "").Replace("Zone", "") : zoneName));
			if (zoneName == null)
			{
				TryAddBoss(bossName, __instance.BornZone.Replace("Bot", "").Replace("Zone", ""));
			}
			else if (zoneName.Equals(""))
			{
				TryAddBoss(bossName, "");
			}
			else
			{
				TryAddBoss(bossName, zoneName);
			}
		}
	}
}
