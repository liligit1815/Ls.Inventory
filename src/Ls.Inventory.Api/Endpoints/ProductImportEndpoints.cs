using Ls.Inventory.Api.Data;
using Ls.Inventory.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Ls.Inventory.Api.Endpoints;

public static class ProductImportEndpoints
{
    public static void MapProductImportEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/products/import").RequireAuthorization();
        g.MapGet("/template", () => Results.File(Path.Combine(AppContext.BaseDirectory, "Templates", "商品信息及现有库存模板.xlsx"),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "商品信息及现有库存模板.xlsx"));
        // Manual form reading keeps the existing cookie + header CSRF middleware in charge.
        g.MapPost("/preview", async (HttpContext c) => {
            if (!c.Request.HasFormContentType) throw new BusinessRuleException("INVALID_FILE", "请选择 Excel 文件");
            var form = await c.Request.ReadFormAsync(c.RequestAborted);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0 || form.Files.Count != 1) throw new BusinessRuleException("INVALID_FILE", "请选择一个非空 Excel 文件");
            if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                throw new BusinessRuleException("INVALID_FILE", "仅支持 .xlsx 文件，旧版 .xls 请另存为 .xlsx");
            if (file.Length > ProductExcelReader.MaxFileBytes) throw new BusinessRuleException("FILE_TOO_LARGE", "Excel 文件不能超过 10 MB", 413);
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, c.RequestAborted); stream.Position = 0;
            return EndpointSupport.Ok(c, ProductExcelReader.Read(stream, c.RequestAborted));
        }).WithMetadata(new RequestSizeLimitAttribute(11 * 1024 * 1024));

        g.MapPost("/confirm", async (ImportRequest request, HttpContext c, InventoryDbContext db) => {
            if (request.RequestId == Guid.Empty) throw new BusinessRuleException("INVALID_REQUEST", "缺少导入请求编号，请重新选择文件");
            if (request.Rows is null || request.Rows.Length is 0 or > 5000)
                throw new BusinessRuleException("INVALID_ROWS", "请选择 1 至 5000 种商品");
            var candidates = request.Rows.Select((r, index) => {
                if (r is null) throw new BusinessRuleException("INVALID_ROWS", $"第 {index + 1} 项商品无效");
                var p = new Product {
                    Name = InventoryRules.Text(r.Name, 200, $"第 {index + 1} 项商品名称", true),
                    ProductSpecification = InventoryRules.Text(r.ProductSpecification, 100, "产品规格"),
                    RawMaterialSpecification = InventoryRules.Text(r.RawMaterialSpecification, 100, "原料规格"),
                    Unit = InventoryRules.Text(r.Unit, 30, $"第 {index + 1} 项单位", true)
                };
                return (Product: p, Input: r);
            }).ToArray();
            var fileName = InventoryRules.Text(request.FileName, 255, "文件名");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { fileName, rows = request.Rows }))));
            var userId = EndpointSupport.UserId(c);
            await using var tx = await db.Database.BeginTransactionAsync(c.RequestAborted);
            await ProductEndpoints.LockCatalog(db);
            // An immutable audit receipt supplies idempotency without another business module/table.
            var requestText = request.RequestId.ToString();
            var receipt = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Action == "Excel 导入商品及库存" && x.Detail.Contains(requestText), c.RequestAborted);
            if (receipt is not null) {
                var saved = JsonSerializer.Deserialize<ImportReceipt>(receipt.Detail)!;
                if (receipt.UserId != userId || saved.Hash != hash) throw new BusinessRuleException("REQUEST_CONFLICT", "该导入已处理，不能更改内容后重复提交", 409);
                return EndpointSupport.Ok(c, new { created = saved.Created, skipped = saved.Skipped,
                    stocktakeId = saved.Created + saved.Updated > 0 ? (Guid?)saved.RequestId : null, alreadyPosted = true });
            }
            if (await db.Stocktakes.AnyAsync(x => x.Id == request.RequestId)) throw new BusinessRuleException("REQUEST_CONFLICT", "请求编号已使用，请重新选择文件", 409);
            var existing = await db.Products.AsNoTracking().ToListAsync(c.RequestAborted);
            var existingKeys = existing.Select(Key).ToHashSet();
            var newKeys = new HashSet<(string, string, string, string)>();
            var added = new List<Product>();
            var targets = new List<(Product Product, ImportRow Input)>();
            var skipped = 0;
            foreach (var item in candidates) {
                // Check the current database under the catalog lock, not the browser's preview.
                // Existing products (including inactive ones) are never modified by import.
                if (existingKeys.Contains(Key(item.Product))) { skipped++; continue; }
                if (!newKeys.Add(Key(item.Product))) throw new BusinessRuleException("DUPLICATE_PRODUCT", "同一新商品重复出现，请保留一条并核实现有库存；库存不会相加");
                var p = item.Product; var r = item.Input;
                if ((p.ProductSpecification == "" || p.RawMaterialSpecification == "") && !r.AllowEmptySpecifications)
                    throw new BusinessRuleException("MISSING_SPECIFICATION", $"{p.Name} 规格缺失，请补全或明确确认无规格");
                if (r.CurrentQuantity is null) throw new BusinessRuleException("MISSING_STOCK", $"{p.Name} 现有库存必填，零库存需明确填写 0");
                InventoryRules.Quantity(r.CurrentQuantity.Value, true);
                p.Note = InventoryRules.Text(r.Note, 500, "备注");
                p.Code = "P" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
                added.Add(p); targets.Add(item);
            }
            if (existing.Count + added.Count > 5000) throw new BusinessRuleException("PRODUCT_LIMIT", "导入后超过轻量版 5000 种商品上限，请减少勾选");
            var stocktake = new Stocktake { Id = request.RequestId, UserId = userId, Status = "Posted", PostedAt = DateTimeOffset.UtcNow };
            foreach (var item in targets.OrderBy(x => x.Product.Id)) {
                var p = item.Product;
                var target = item.Input.CurrentQuantity!.Value;
                stocktake.Lines.Add(new StocktakeLine { ProductId = p.Id, ExpectedQuantity = p.Quantity, ExpectedVersion = p.Version, CountedQuantity = target });
                var difference = target - p.Quantity;
                if (difference != 0) {
                    p.Quantity = target; p.Version++;
                    db.Movements.Add(new Movement { RequestId = Guid.NewGuid(), ProductId = p.Id, UserId = userId,
                        Kind = difference > 0 ? "StockGain" : "StockLoss", QuantityChange = difference, QuantityAfter = target,
                        Note = "Excel 新商品现有库存导入", StocktakeId = stocktake.Id });
                }
            }
            db.Products.AddRange(added);
            if (added.Count > 0) db.Stocktakes.Add(stocktake);
            EndpointSupport.Audit(db, c, "Excel 导入商品及库存", new ImportReceipt(request.RequestId, hash, fileName, added.Count, Skipped: skipped));
            await db.SaveChangesAsync(c.RequestAborted); await tx.CommitAsync(c.RequestAborted);
            return EndpointSupport.Ok(c, new { created = added.Count, skipped, stocktakeId = added.Count > 0 ? (Guid?)stocktake.Id : null, alreadyPosted = false });
        });
    }
    private static (string, string, string, string) Key(Product p) => (p.Name, p.ProductSpecification, p.RawMaterialSpecification, p.Unit);
    public sealed record ImportRow(string Name, string? ProductSpecification, string? RawMaterialSpecification, string Unit,
        int? CurrentQuantity, string? Note, bool AllowEmptySpecifications = false);
    public sealed record ImportRequest(Guid RequestId, string? FileName, ImportRow[]? Rows);
    // Updated is retained only to read immutable receipts created before the skip-only rule.
    public sealed record ImportReceipt(Guid RequestId, string Hash, string FileName, int Created, int Updated = 0, int Skipped = 0);
}
