using System;
using System.Reflection;
using BepInEx.Logging;
using Comfort.Common;
using EFT;

namespace BossNotifier;

/// <summary>
/// Fika 联机环境探测（软依赖：没有安装 Fika 时全部退化为单机语义）。
///
/// 注意 Fika 的枚举顺序是 None = 0, Client = 1, Host = 2。
/// 原版 BossNotifier 用 <c>ClientType == 2</c> 判断“本进程该不该工作”，
/// 实际含义变成了“只有房主才工作”。而 Fika 无头主机（headless）没有本地玩家和摄像机，
/// 唯一能显示提示的是加入房间的客户端（ClientType = 1），于是提示全都消失了。
/// </summary>
internal static class FikaCompat
{
	public const int ClientTypeNone = 0;

	public const int ClientTypeClient = 1;

	public const int ClientTypeHost = 2;

	private const BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

	private static bool _resolved;

	private static PropertyInfo _clientTypeProperty;

	private static PropertyInfo _isHeadlessProperty;

	private static PropertyInfo _isHeadlessGameProperty;

	public static void Resolve()
	{
		if (_resolved)
		{
			return;
		}
		_resolved = true;
		try
		{
			Type type = Type.GetType("Fika.Core.Main.Utils.FikaBackendUtils, Fika.Core", throwOnError: false);
			if (type == null)
			{
				BossNotifierPlugin.Log(LogLevel.Info, "Fika not detected - single player mode");
				return;
			}
			_clientTypeProperty = type.GetProperty("ClientType", StaticFlags);
			_isHeadlessProperty = type.GetProperty("IsHeadless", StaticFlags);
			_isHeadlessGameProperty = type.GetProperty("IsHeadlessGame", StaticFlags);
			if (_clientTypeProperty != null)
			{
				BossNotifierPlugin.FikaClientTypeProperty = _clientTypeProperty;
			}
			BossNotifierPlugin.Log(LogLevel.Info, $"Fika detected - ClientType={ClientType} localHeadless={LocalHeadless} hostHeadless={HostIsHeadless}");
		}
		catch (Exception ex)
		{
			BossNotifierPlugin.Log(LogLevel.Warning, "Fika probe failed: " + ex.Message);
		}
	}

	public static bool IsFikaPresent
	{
		get
		{
			Resolve();
			return _clientTypeProperty != null;
		}
	}

	/// <summary>0 = None，1 = Client，2 = Host；没有 Fika 时返回 -1。</summary>
	public static int ClientType
	{
		get
		{
			Resolve();
			if (_clientTypeProperty == null)
			{
				return -1;
			}
			try
			{
				object value = _clientTypeProperty.GetValue(null, null);
				if (value == null)
				{
					return -1;
				}
				return Convert.ToInt32(value);
			}
			catch
			{
				return -1;
			}
		}
	}

	public static bool IsClient => ClientType == ClientTypeClient;

	public static bool IsHost => ClientType == ClientTypeHost;

	/// <summary>
	/// 本进程自己是不是 Fika 无头服务端（FikaBackendUtils.IsHeadless）。
	///
	/// 注意：<b>不要</b>把 FikaBackendUtils.IsHeadlessGame 算进来。
	/// IsHeadlessGame 的含义是“本局游戏的房主是无头服务端”
	/// （见 FikaPingingClient.Init 里的 <c>FikaBackendUtils.IsHeadlessGame = host.IsHeadless;</c>），
	/// 加入无头房间的客户端 Gets true，但它自己是有屏幕有玩家的正常客户端。
	/// 早期版本把两者混为一谈，导致“进无头房就整个插件静默”。
	/// </summary>
	public static bool LocalHeadless
	{
		get
		{
			Resolve();
			return ReadBool(_isHeadlessProperty);
		}
	}

	/// <summary>本局游戏的房主是无头服务端（FikaBackendUtils.IsHeadlessGame）。仅用于日志/行为微调。</summary>
	public static bool HostIsHeadless
	{
		get
		{
			Resolve();
			return ReadBool(_isHeadlessGameProperty);
		}
	}

	/// <summary>本机是否有本地玩家（= 有 HUD、能显示提示）。无头服务端永远是 null。</summary>
	public static bool HasLocalPlayer
	{
		get
		{
			GameWorld world = World;
			if ((object)world == null)
			{
				return false;
			}
			return (object)world.MainPlayer != null;
		}
	}

	/// <summary>
	/// 本进程能不能显示 UI（提示 / 屏幕标记）。这是所有 UI 路径的唯一闸门：
	/// 真·无头进程 → 不能；还没进战局（没有本地玩家）→ 暂时不能。
	/// 用“有没有本地玩家”而不是 Fika 的 ClientType 判断，因为那才是本质条件。
	/// </summary>
	public static bool CanRenderUi
	{
		get
		{
			if (LocalHeadless)
			{
				return false;
			}
			return HasLocalPlayer;
		}
	}

	private static bool ReadBool(PropertyInfo property)
	{
		if (property == null)
		{
			return false;
		}
		try
		{
			object value = property.GetValue(null, null);
			return value is bool flag && flag;
		}
		catch
		{
			return false;
		}
	}

	public static GameWorld World
	{
		get
		{
			if (!Singleton<GameWorld>.Instantiated)
			{
				return null;
			}
			return Singleton<GameWorld>.Instance;
		}
	}

	public static Player LocalPlayer
	{
		get
		{
			GameWorld world = World;
			if ((object)world == null)
			{
				return null;
			}
			return world.MainPlayer;
		}
	}
}
