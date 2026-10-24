using System.Collections.Generic;

namespace BossNotifier;

public class CustomBossGroup
{
	public string groupName { get; set; }

	public string pluginGUID { get; set; }

	public bool isPlural { get; set; } = true;

	public List<CustomBossMember> members { get; set; }
}
