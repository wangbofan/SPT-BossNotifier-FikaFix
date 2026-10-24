using System.Reflection;
using EFT;
using SPT.Reflection.Patching;

namespace BossNotifier;

internal class NewGamePatch : ModulePatch
{
	protected override MethodBase GetTargetMethod()
	{
		return typeof(GameWorld).GetMethod("OnGameStarted");
	}

	[PatchPrefix]
	public static void PatchPrefix()
	{
		BossNotifierMono.Init();
	}
}
