using System.Text.Json;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ls.Inventory.Api.Data;

public sealed record ProductDetails(string Code, string Name, string Category, string ProductSpecification,
    string RawMaterialSpecification, string Unit, string Note, bool IsActive, int? WarningQuantity)
{
    public static ProductDetails From(Product p) => new(p.Code, p.Name, p.Category, p.ProductSpecification,
        p.RawMaterialSpecification, p.Unit, p.Note, p.IsActive, p.WarningQuantity);
}

public sealed class ProductDetailsConverter() : ValueConverter<ProductDetails, string>(
    value => JsonSerializer.Serialize(value, JsonSerializerOptions.Default),
    json => JsonSerializer.Deserialize<ProductDetails>(json, JsonSerializerOptions.Default)!);

public sealed class ProductHistory
{
    public long Id { get; set; }
    public Guid ProductId { get; set; }
    public DateTimeOffset ChangedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UserName { get; set; } = "";
    public int Quantity { get; set; }
    public ProductDetails? Before { get; set; }
    public required ProductDetails After { get; set; }

    public static void Record(InventoryDbContext db, Product product, ProductDetails? before, string userName)
    {
        var after = ProductDetails.From(product);
        if (after == before) return;
        db.ProductHistories.Add(new ProductHistory {
            ProductId = product.Id, UserName = userName, Quantity = product.Quantity, Before = before, After = after
        });
    }
}
