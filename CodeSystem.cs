using System.Text;
using Newtonsoft.Json;
using TShockAPI;

namespace GiftCodePlugin;

public class ItemStack
{
    [JsonProperty("物品ID")]
    public int ItemId { get; set; }

    [JsonProperty("数量")]
    public int Stack { get; set; }

    public ItemStack() { }
    public ItemStack(int id, int stack) { ItemId = id; Stack = stack; }
}

public class RedemptionCode
{
    [JsonProperty("礼包码")]
    public string Code { get; set; } = string.Empty;

    [JsonProperty("物品列表")]
    public List<ItemStack> Items { get; set; } = new();

    [JsonProperty("总可用次数", NullValueHandling = NullValueHandling.Ignore)]
    public int? MaxUses { get; set; }

    [JsonProperty("已使用次数")]
    public int UsedCount { get; set; } = 0;

    [JsonProperty("每个玩家最多领取次数", NullValueHandling = NullValueHandling.Ignore)]
    public int? PerPlayerLimit { get; set; }

    [JsonProperty("过期时间", NullValueHandling = NullValueHandling.Ignore)]
    public DateTime? ExpireTime { get; set; }

    [JsonProperty("创建者")]
    public string Creator { get; set; } = string.Empty;

    [JsonProperty("创建时间")]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    [JsonProperty("玩家领取记录")]
    public Dictionary<string, int> PlayerUsedCount { get; set; } = new();

    public bool IsValid(out string errorMsg)
    {
        if (MaxUses.HasValue && UsedCount >= MaxUses.Value)
        {
            errorMsg = "该礼包码已达到使用次数上限。";
            return false;
        }
        if (ExpireTime.HasValue && DateTime.UtcNow > ExpireTime.Value)
        {
            errorMsg = "该礼包码已过期。";
            return false;
        }
        errorMsg = string.Empty;
        return true;
    }

    public bool CanPlayerRedeem(string playerName, out string errorMsg)
    {
        if (!PerPlayerLimit.HasValue)
        {
            errorMsg = string.Empty;
            return true;
        }
        int used = PlayerUsedCount.GetValueOrDefault(playerName, 0);
        if (used >= PerPlayerLimit.Value)
        {
            errorMsg = $"你已领取过该礼包码 {PerPlayerLimit.Value} 次，不能再领取了。";
            return false;
        }
        errorMsg = string.Empty;
        return true;
    }

    public void RecordPlayer(string playerName)
    {
        if (PerPlayerLimit.HasValue)
            PlayerUsedCount[playerName] = PlayerUsedCount.GetValueOrDefault(playerName, 0) + 1;
        UsedCount++;
    }
}

internal static class CodeManager
{
    private static Dictionary<string, RedemptionCode> _codes = new();

    public static void Load()
    {
        if (!File.Exists(Plugin.DataFile))
        {
            _codes = new Dictionary<string, RedemptionCode>();
            Save();
            return;
        }
        try
        {
            string json = File.ReadAllText(Plugin.DataFile);
            _codes = JsonConvert.DeserializeObject<Dictionary<string, RedemptionCode>>(json)
                     ?? new Dictionary<string, RedemptionCode>();
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{Plugin.PluginName}] 加载礼包码数据失败: {ex.Message}");
            _codes = new Dictionary<string, RedemptionCode>();
        }
    }

    public static void Save()
    {
        try
        {
            string json = JsonConvert.SerializeObject(_codes, Formatting.Indented);
            File.WriteAllText(Plugin.DataFile, json);
        }
        catch (Exception ex)
        {
            TShock.Log.ConsoleError($"[{Plugin.PluginName}] 保存礼包码数据失败: {ex.Message}");
        }
    }

    public static bool CreateCode(RedemptionCode code, out string errorMsg)
    {
        if (_codes.ContainsKey(code.Code))
        {
            errorMsg = $"礼包码 '{code.Code}' 已存在。";
            return false;
        }
        if (code.Items == null || code.Items.Count == 0)
        {
            errorMsg = "礼包码至少包含一个物品。";
            return false;
        }
        _codes[code.Code] = code;
        Save();
        errorMsg = string.Empty;
        return true;
    }

    public static bool DeleteCode(string code, out string errorMsg)
    {
        if (!_codes.ContainsKey(code))
        {
            errorMsg = $"礼包码 '{code}' 不存在。";
            return false;
        }
        _codes.Remove(code);
        Save();
        errorMsg = string.Empty;
        return true;
    }

    public static List<RedemptionCode> GetAllCodes() => _codes.Values.ToList();

    public static RedemptionCode? GetCode(string code) =>
        _codes.TryGetValue(code, out var c) ? c : null;

    public static bool RedeemCode(TSPlayer player, string code, out string errorMsg)
    {
        if (!_codes.TryGetValue(code, out var redemption))
        {
            errorMsg = "礼包码不存在。";
            return false;
        }

        if (!redemption.IsValid(out errorMsg))
            return false;

        if (!redemption.CanPlayerRedeem(player.Name, out errorMsg))
            return false;

        int emptySlots = player.TPlayer.inventory.Count(item => item is null || item.type == 0);
        if (emptySlots < redemption.Items.Count)
        {
            errorMsg = $"背包空间不足，至少需要 {redemption.Items.Count} 个空格。";
            return false;
        }

        foreach (var itemStack in redemption.Items)
        {
            if (itemStack.ItemId <= 0 || itemStack.Stack <= 0) continue;
            player.GiveItem(itemStack.ItemId, itemStack.Stack);
        }

        redemption.RecordPlayer(player.Name);
        Save();
        errorMsg = string.Empty;
        return true;
    }
}

public static class CodeCommands
{
    private const string PermissionAdmin = "code.admin";
    private const string PermissionUse = "code.use";

    public static void Register()
    {
        Commands.ChatCommands.Add(new Command(PermissionAdmin, CmdCreate, "code", "礼包码")
        {
            HelpText = "管理礼包码。使用 /code help 查看子命令。"
        });
        Commands.ChatCommands.Add(new Command(PermissionUse, CmdRedeem, "redeem", "领取")
        {
            HelpText = "使用礼包码：/redeem <礼包码>"
        });
    }

    private static void CmdCreate(CommandArgs args)
    {
        var subCmd = args.Parameters.Count > 0 ? args.Parameters[0].ToLower() : "";
        switch (subCmd)
        {
            case "create":
            case "new":
                CreateNewCode(args);
                break;
            case "delete":
            case "del":
                DeleteCode(args);
                break;
            case "list":
                ListCodes(args);
                break;
            case "help":
                SendHelp(args.Player);
                break;
            default:
                SendHelp(args.Player);
                break;
        }
    }

    private static void CreateNewCode(CommandArgs args)
    {
        if (args.Parameters.Count < 3)
        {
            args.Player.SendErrorMessage("用法: /code create <礼包码> <物品ID:数量,物品ID:数量> [总次数] [每人次数] [过期时间(YYYY-MM-DD HH:MM)]");
            args.Player.SendErrorMessage("示例: /code create GIFT001 1:100,2:50 10 1 2026-12-31 23:59");
            return;
        }

        string code = args.Parameters[1];
        string itemsStr = args.Parameters[2];
        int? maxUses = null;
        int? perPlayerLimit = null;
        DateTime? expireTime = null;

        if (args.Parameters.Count > 3 && int.TryParse(args.Parameters[3], out int mu))
            maxUses = mu;
        if (args.Parameters.Count > 4 && int.TryParse(args.Parameters[4], out int ppl))
            perPlayerLimit = ppl;
        if (args.Parameters.Count > 5)
        {
            string dateStr = args.Parameters[5];
            string timeStr = args.Parameters.Count > 6 ? args.Parameters[6] : "00:00";
            if (DateTime.TryParse($"{dateStr} {timeStr}", out DateTime dt))
                expireTime = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        }

        var items = new List<ItemStack>();
        foreach (var part in itemsStr.Split(','))
        {
            var idStack = part.Split(':');
            if (idStack.Length != 2 ||
                !int.TryParse(idStack[0], out int id) ||
                !int.TryParse(idStack[1], out int stack) ||
                stack <= 0)
            {
                args.Player.SendErrorMessage($"物品格式错误: {part}，应为 ID:数量");
                return;
            }
            items.Add(new ItemStack(id, stack));
        }

        var redemption = new RedemptionCode
        {
            Code = code,
            Items = items,
            MaxUses = maxUses,
            PerPlayerLimit = perPlayerLimit,
            ExpireTime = expireTime,
            Creator = args.Player.Name,
            CreateTime = DateTime.UtcNow
        };

        if (CodeManager.CreateCode(redemption, out string err))
        {
            args.Player.SendSuccessMessage($"礼包码 [{code}] 创建成功！");
            SendCodeInfo(args.Player, redemption);
        }
        else
        {
            args.Player.SendErrorMessage(err);
        }
    }

    private static void DeleteCode(CommandArgs args)
    {
        if (args.Parameters.Count < 2)
        {
            args.Player.SendErrorMessage("用法: /code delete <礼包码>");
            return;
        }
        string code = args.Parameters[1];
        if (CodeManager.DeleteCode(code, out string err))
            args.Player.SendSuccessMessage($"礼包码 [{code}] 已删除。");
        else
            args.Player.SendErrorMessage(err);
    }

    private static void ListCodes(CommandArgs args)
    {
        var codes = CodeManager.GetAllCodes();
        if (codes.Count == 0)
        {
            args.Player.SendInfoMessage("当前没有任何礼包码。");
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("=== 礼包码列表 ===");
        foreach (var c in codes)
        {
            sb.AppendLine($"码: {c.Code} | 已用/总: {c.UsedCount}/{(c.MaxUses.HasValue ? c.MaxUses.ToString() : "∞")} | 每人限: {(c.PerPlayerLimit.HasValue ? c.PerPlayerLimit.ToString() : "不限")} | 过期: {(c.ExpireTime.HasValue ? c.ExpireTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "永久")}");
        }
        args.Player.SendMessage(sb.ToString(), Utils.Color);
    }

    private static void SendHelp(TSPlayer player)
    {
        var sb = new StringBuilder();
        sb.AppendLine("礼包码指令帮助:");
        sb.AppendLine("/code create <码> <物品ID:数量,ID:数量> [总次数] [每人次数] [过期日期] [过期时间] - 创建礼包码");
        sb.AppendLine("/code delete <码> - 删除礼包码");
        sb.AppendLine("/code list - 查看所有礼包码");
        sb.AppendLine("/redeem <码> - 领取礼包码");
        player.SendMessage(sb.ToString(), Utils.Color);
    }

    private static void SendCodeInfo(TSPlayer player, RedemptionCode code)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"礼包码: {code.Code}");
        sb.AppendLine("物品:");
        foreach (var item in code.Items)
        {
            // 修正：使用 GetItemById 获取 Item 对象，再获取名称
            var terrariaItem = TShock.Utils.GetItemById(item.ItemId);
            string itemName = terrariaItem?.Name ?? $"未知物品 (ID:{item.ItemId})";
            sb.AppendLine($"  - {itemName} x{item.Stack} (ID:{item.ItemId})");
        }
        sb.AppendLine($"总次数: {(code.MaxUses.HasValue ? code.MaxUses.ToString() : "无限")}");
        sb.AppendLine($"每人限领: {(code.PerPlayerLimit.HasValue ? code.PerPlayerLimit.ToString() : "不限")}");
        sb.AppendLine($"过期时间: {(code.ExpireTime.HasValue ? code.ExpireTime.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "永久")}");
        sb.AppendLine($"创建者: {code.Creator}");
        player.SendMessage(sb.ToString(), Utils.Color);
    }

    private static void CmdRedeem(CommandArgs args)
    {
        if (args.Parameters.Count < 1)
        {
            args.Player.SendErrorMessage("用法: /redeem <礼包码>");
            return;
        }
        string code = args.Parameters[0];
        if (CodeManager.RedeemCode(args.Player, code, out string err))
        {
            args.Player.SendSuccessMessage($"成功领取礼包码 [{code}]！");
            TSPlayer.All.SendMessage($"[{Plugin.PluginName}] 玩家 {args.Player.Name} 领取了礼包码 {code}！", Utils.Color);
        }
        else
        {
            args.Player.SendErrorMessage(err);
        }
    }
}