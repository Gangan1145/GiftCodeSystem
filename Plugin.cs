using Terraria;
using TShockAPI;
using TerrariaApi.Server;

namespace GiftCodePlugin;

[ApiVersion(2, 1)]
public class Plugin : TerrariaPlugin
{
    public static string PluginName => "礼包码系统";
    public override string Name => PluginName;
    public override string Author => "淦";
    public override Version Version => new(1, 0, 0);
    public override string Description => "管理员发布礼包码，玩家兑换物品";

    public static readonly string MainPath = Path.Combine(TShock.SavePath, PluginName);
    public static readonly string DataFile = Path.Combine(MainPath, "礼包码数据.json");

    public Plugin(Main game) : base(game) { }

    public override void Initialize()
    {
        if (!Directory.Exists(MainPath))
            Directory.CreateDirectory(MainPath);

        CodeManager.Load();
        CodeCommands.Register();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            CodeManager.Save();
        base.Dispose(disposing);
    }
}