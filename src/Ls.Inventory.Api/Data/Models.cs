namespace Ls.Inventory.Api.Data;

public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool MustChangePassword { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
public sealed class LoginSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
    public bool Revoked { get; set; }
}
public sealed class Product
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProductSpecification { get; set; } = "";
    public string RawMaterialSpecification { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Note { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int Quantity { get; set; }
    public int? WarningQuantity { get; set; }
    public long Version { get; set; } = 1;
}
// Every row is posted and immutable; QuantityAfter is the balance after this row.
public sealed class Movement
{
    public long Id { get; set; }
    public Guid RequestId { get; set; }
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public string Kind { get; set; } = "";
    public int QuantityChange { get; set; }
    public int QuantityAfter { get; set; }
    public string Note { get; set; } = "";
    public DateTimeOffset PostedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public Guid? StocktakeId { get; set; }
    public Stocktake? Stocktake { get; set; }
}
public sealed class Stocktake
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Status { get; set; } = "Draft";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PostedAt { get; set; }
    public Guid UserId { get; set; }
    public AppUser User { get; set; } = null!;
    public List<StocktakeLine> Lines { get; set; } = [];
}
public sealed class StocktakeLine
{
    public Guid StocktakeId { get; set; }
    public Stocktake Stocktake { get; set; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
    public int ExpectedQuantity { get; set; }
    public long ExpectedVersion { get; set; }
    public int? CountedQuantity { get; set; }
}
public sealed class AuditLog
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Detail { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string TraceId { get; set; } = "";
}
