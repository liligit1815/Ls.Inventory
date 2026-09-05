namespace Ls.Inventory.Api;
public sealed class BusinessRuleException(string code, string message, int statusCode = 400) : Exception(message)
{
    public string ErrorCode { get; } = code;
    public int StatusCode { get; } = statusCode;
}
public static class InventoryRules
{
    public static void Quantity(decimal quantity, bool allowZero = false)
    {
        if (quantity < 0 || (!allowZero && quantity == 0) || quantity > int.MaxValue || decimal.Truncate(quantity) != quantity)
            throw new BusinessRuleException("INVALID_QUANTITY", "数量必须是 0 至 2147483647 的整数；出入库数量必须大于零");
    }
    public static int Apply(int stock, int change)
    {
        var result = (long)stock + change;
        if (result < 0) throw new BusinessRuleException("INSUFFICIENT_STOCK", $"库存不足，当前可用 {stock}");
        Quantity(result, true);
        return (int)result;
    }
    public static string Text(string? value, int max, string name, bool required = false)
    {
        var result = value?.Trim() ?? "";
        if ((required && result.Length == 0) || result.Length > max)
            throw new BusinessRuleException("INVALID_FIELD", $"{name}{(required ? "必填，且" : "")}不能超过 {max} 字");
        return result;
    }
}
