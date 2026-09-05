using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
using Ls.Inventory.Api;
using Ls.Inventory.Api.Endpoints;
using Ls.Inventory.Api.Services;
using Xunit;

namespace Ls.Inventory.IntegrationTests;

public sealed class ProductExcelReaderTests
{
    internal static string Template => Path.Combine(AppContext.BaseDirectory, "Fixtures", "商品信息及现有库存模板.xlsx");
    [Fact]
    public void Public_template_has_required_headers_and_no_business_rows()
    {
        using var stream = File.OpenRead(Template);
        using (var reader = ExcelDataReader.ExcelReaderFactory.CreateOpenXmlReader(stream, new ExcelDataReader.ExcelReaderConfiguration { LeaveOpen = true }))
        {
            Assert.True(reader.Read());
            Assert.Equal(new[] { "序号", "产品名称", "产品规格", "原料规格", "单位", "现有库存", "备注" },
                Enumerable.Range(0, 7).Select(i => reader.GetString(i)).ToArray());
        }
        stream.Position = 0;
        Assert.Equal("NO_PRODUCTS", Assert.Throws<BusinessRuleException>(() => ProductExcelReader.Read(stream)).ErrorCode);
    }
    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("132")]
    public void Stock_cells_preserve_blank_zero_quantity_and_notes(string quantity)
    {
        using var stream = StockWorkbook(quantity);
        var row = Assert.Single(ProductExcelReader.Read(stream).Rows);
        Assert.Equal(quantity, row.CurrentQuantity);
        Assert.Equal("测试备注", row.Note);
    }
    internal static MemoryStream StockWorkbook(string quantity)
    {
        // A deterministic in-memory fixture keeps quantity coverage independent of the user's template.
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(content);
            }
            Add("[Content_Types].xml", """
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Add("_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
                """);
            Add("xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="商品信息" sheetId="1" r:id="rId1"/></sheets></workbook>
                """);
            Add("xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>
                """);
            Add("xl/worksheets/sheet1.xml", $$"""
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
                  <row r="1"><c r="A1" t="inlineStr"><is><t>产品名称</t></is></c><c r="B1" t="inlineStr"><is><t>现有库存</t></is></c><c r="C1" t="inlineStr"><is><t>备注</t></is></c></row>
                  <row r="2"><c r="A2" t="inlineStr"><is><t>测试商品</t></is></c>{{(quantity == "" ? "" : $"<c r=\"B2\"><v>{quantity}</v></c>")}}<c r="C2" t="inlineStr"><is><t>测试备注</t></is></c></row>
                </sheetData></worksheet>
                """);
        }
        stream.Position = 0;
        return stream;
    }
    [Fact]
    public void Broken_file_has_a_readable_validation_error()
    {
        using var stream = new MemoryStream([1,2,3,4]);
        Assert.Equal("INVALID_EXCEL", Assert.Throws<BusinessRuleException>(() => ProductExcelReader.Read(stream)).ErrorCode);
    }
}

public sealed partial class LiteFlowTests
{
    [Fact]
    public async Task Import_snapshot_posts_atomically_and_retries_without_reapplying_stock()
    {
        using var c = await Login();
        using var file = new MultipartFormDataContent();
        using var sample = ProductExcelReaderTests.StockWorkbook("10");
        file.Add(new ByteArrayContent(sample.ToArray()), "file", "商品信息及现有库存模板.xlsx");
        var preview = await Data(await c.PostAsync("/api/v1/products/import/preview", file));
        Assert.Equal(1, preview.GetProperty("rows").GetArrayLength());
        Assert.Equal("10", preview.GetProperty("rows")[0].GetProperty("currentQuantity").GetString());
        Assert.Equal("测试备注", preview.GetProperty("rows")[0].GetProperty("note").GetString());
        var name = "导入-" + Guid.NewGuid().ToString("N");
        var row = new ProductImportEndpoints.ImportRow(name, "2.3", "铝", "箱", 10, "说明");
        var request = new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "test.xlsx", [row]);
        var result = await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", request));
        Assert.Equal(1, result.GetProperty("created").GetInt32());
        var products = await Data(await c.GetAsync("/api/v1/products/"));
        var product = products.EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        var id = product.GetProperty("id").GetGuid();
        var report = await Data(await c.GetAsync($"/api/v1/analytics?productId={id}"));
        var totals = report.GetProperty("rows")[0];
        Assert.Equal(10, totals.GetProperty("currentQuantity").GetDecimal());
        Assert.Equal(10, totals.GetProperty("adjustment").GetDecimal());
        await Data(await Move(c, id, "Outbound", 3));
        Assert.True((await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", request))).GetProperty("alreadyPosted").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await c.PostAsJsonAsync("/api/v1/products/import/confirm", request with { Rows = [row with { CurrentQuantity = 20 }] })).StatusCode);
        var fresh = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(7, fresh.GetProperty("quantity").GetDecimal());
        var adjusted = new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "again.xlsx", [row with { CurrentQuantity = 2, Note = "不能覆盖原备注" }]);
        var skipped = await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", adjusted));
        Assert.Equal(0, skipped.GetProperty("created").GetInt32()); Assert.Equal(1, skipped.GetProperty("skipped").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, skipped.GetProperty("stocktakeId").ValueKind);
        Assert.True((await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", adjusted))).GetProperty("alreadyPosted").GetBoolean());
        report = await Data(await c.GetAsync($"/api/v1/analytics?productId={id}"));
        Assert.Equal(7, report.GetProperty("rows")[0].GetProperty("currentQuantity").GetDecimal());
        var unchanged = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal("说明", unchanged.GetProperty("note").GetString());
        Assert.Equal(fresh.GetProperty("version").GetInt64(), unchanged.GetProperty("version").GetInt64());
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={id}"));
        Assert.Equal(2, ledger.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/stocktakes/{adjusted.RequestId}")).StatusCode);
        var mixed = new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "mixed.xlsx", [row with { CurrentQuantity = null }, row with { Unit = "个", CurrentQuantity = 4, Note = "新商品备注" }]);
        var mixedResult = await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", mixed));
        Assert.Equal(1, mixedResult.GetProperty("created").GetInt32()); Assert.Equal(1, mixedResult.GetProperty("skipped").GetInt32());
        var second = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("name").GetString() == name && x.GetProperty("unit").GetString() == "个");
        Assert.Equal(4, second.GetProperty("quantity").GetDecimal()); Assert.Equal("新商品备注", second.GetProperty("note").GetString());
    }
    [Fact]
    public async Task Import_rejects_incomplete_negative_duplicate_and_unauthorized_requests()
    {
        using var c = await Login();
        var name = "不应部分创建-" + Guid.NewGuid().ToString("N");
        var good = new ProductImportEndpoints.ImportRow(name, "A", "B", "个", 0, "");
        foreach (var bad in new[] { good with { Name = "" }, good with { CurrentQuantity = null }, good with { CurrentQuantity = -1 }, good with { ProductSpecification = "" } }) {
            var req = new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "bad.xlsx", [good, bad]);
            Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/products/import/confirm", req)).StatusCode);
        }
        Assert.DoesNotContain((await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray(), x => x.GetProperty("name").GetString() == name);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/v1/products/import/confirm", new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "duplicate.xlsx", [good, good]))).StatusCode);
        var zero = good with { ProductSpecification = "", RawMaterialSpecification = "", AllowEmptySpecifications = true };
        await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "zero.xlsx", [zero])));
        var created = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("name").GetString() == name);
        Assert.Equal(0, created.GetProperty("quantity").GetDecimal());
        var ledger = await Data(await c.GetAsync($"/api/v1/movements?productId={created.GetProperty("id").GetGuid()}"));
        Assert.Equal(0, ledger.GetProperty("total").GetInt32());
        var zeroId = created.GetProperty("id").GetGuid();
        await Data(await c.PutAsJsonAsync($"/api/v1/products/{zeroId}", new { name, productSpecification = "", rawMaterialSpecification = "", unit = "个", note = "保留", isActive = false, version = created.GetProperty("version").GetInt64() }));
        var inactiveSkip = await Data(await c.PostAsJsonAsync("/api/v1/products/import/confirm", new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "inactive.xlsx", [zero with { CurrentQuantity = -10, AllowEmptySpecifications = false }])));
        Assert.Equal(1, inactiveSkip.GetProperty("skipped").GetInt32());
        var inactive = (await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == zeroId);
        Assert.False(inactive.GetProperty("isActive").GetBoolean()); Assert.Equal("保留", inactive.GetProperty("note").GetString()); Assert.Equal(0, inactive.GetProperty("quantity").GetDecimal());
        c.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using var file = new MultipartFormDataContent(); file.Add(new ByteArrayContent([1, 2, 3]), "file", "bad.xlsx");
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/v1/products/import/preview", file)).StatusCode);
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/products/import/confirm", new { })).StatusCode);
    }
    [Fact]
    public async Task Concurrent_imports_of_the_same_new_product_create_once_and_skip_once()
    {
        using var c = await Login();
        var row = new ProductImportEndpoints.ImportRow("并发导入-" + Guid.NewGuid().ToString("N"), "A", "B", "箱", 6, "备注");
        var first = new ProductImportEndpoints.ImportRequest(Guid.NewGuid(), "first.xlsx", [row]);
        var second = first with { RequestId = Guid.NewGuid(), FileName = "second.xlsx" };
        var replies = await Task.WhenAll(c.PostAsJsonAsync("/api/v1/products/import/confirm", first), c.PostAsJsonAsync("/api/v1/products/import/confirm", second));
        var results = await Task.WhenAll(replies.Select(Data));
        Assert.Equal(1, results.Sum(x => x.GetProperty("created").GetInt32())); Assert.Equal(1, results.Sum(x => x.GetProperty("skipped").GetInt32()));
        var p = Assert.Single((await Data(await c.GetAsync("/api/v1/products/"))).EnumerateArray(), x => x.GetProperty("name").GetString() == row.Name);
        Assert.Equal(6, p.GetProperty("quantity").GetDecimal());
        Assert.Equal(1, (await Data(await c.GetAsync($"/api/v1/movements?productId={p.GetProperty("id").GetGuid()}"))).GetProperty("total").GetInt32());
    }
}
