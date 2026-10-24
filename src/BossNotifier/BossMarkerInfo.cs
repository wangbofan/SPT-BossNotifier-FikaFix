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
