using EFT;
using UnityEngine;

namespace BossNotifier;

public class BossMarkerInfo
{
	public Player Player { get; set; }

	public string BossName { get; set; }

	public string GroupName { get; set; }

	public GameObject MarkerObject { get; set; }

	public TextMesh SymbolTextMesh { get; set; }

	public TextMesh InfoTextMesh { get; set; }

	/// <summary>
	/// boss 第一次被看到时的坐标。区域名在开局时可能查不到（BotZone 还没加载完），
	/// 之后补查时用这个坐标而不是当前位置，避免 boss 已经走远、报出错误的区域。
	/// </summary>
	public Vector3 FirstSeenPosition { get; set; }

	public BossMarkerInfo(Player player, string bossName, string groupName, GameObject markerObject, TextMesh symbolTextMesh, TextMesh infoTextMesh)
	{
		Player = player;
		BossName = bossName;
		GroupName = groupName;
		MarkerObject = markerObject;
		SymbolTextMesh = symbolTextMesh;
		InfoTextMesh = infoTextMesh;
	}
}
