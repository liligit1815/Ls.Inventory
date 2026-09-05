using System.Globalization;
using System.IO.Compression;
using ExcelDataReader;

namespace Ls.Inventory.Api.Services;

// Reads a current-stock snapshot. Never derives monthly movements or converts units.
public static class ProductExcelReader
{
    static ProductExcelReader() => System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    public const int MaxFileBytes = 10 * 1024 * 1024;
    public sealed record Row(string Name, string ProductSpecification, string RawMaterialSpecification, string Unit, string CurrentQuantity, string Note, List<string> Sources);
    public sealed record Preview(List<Row> Rows, int SourceRowCount, List<string> SkippedSheets);
    public static Preview Read(Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Count > 5000 || zip.Entries.Sum(e => e.Length) > 100L * 1024 * 1024)
                    throw new BusinessRuleException("EXCEL_TOO_LARGE", "Excel 内容过大，请拆分文件后导入");
            }
            stream.Position = 0;
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream, new ExcelReaderConfiguration { LeaveOpen = true });
            var rows = new List<Row>();
            var skipped = new List<string>();
            var sourceCount = 0;
            var scannedRows = 0;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.VisibleState != "visible") { skipped.Add(reader.Name + "（隐藏）"); continue; }
                int[]? columns = null;
                var rowNumber = 0;
                while (reader.Read())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++scannedRows > 100000 || reader.FieldCount > 512)
                        throw new BusinessRuleException("EXCEL_TOO_LARGE", "工作表行列过多，请仅保留商品相关内容后上传");
                    rowNumber++;
                    if (columns is null)
                    {
                        if (rowNumber > 50) break;
                        var headers = Enumerable.Range(0, reader.FieldCount).Select(i => Cell(reader.GetValue(i)).Replace(" ", "").Replace("\n", "").Replace("\r", "")).ToArray();
                        var name = Array.FindIndex(headers, h => h is "商品名称" or "产品名称");
                        var stock = Array.IndexOf(headers, "现有库存");
                        if (name >= 0 && stock >= 0) columns = [name, Array.IndexOf(headers, "产品规格"), Array.IndexOf(headers, "原料规格"), Array.IndexOf(headers, "单位"), stock, Array.IndexOf(headers, "备注")];
                        continue;
                    }
                    var values = columns.Select(i => i < 0 || i >= reader.FieldCount ? "" : Cell(reader.GetValue(i))).ToArray();
                    if (values.All(string.IsNullOrEmpty) || values[0] is "合计" or "总计" or "小计" or "商品名称" or "产品名称") continue;
                    if (++sourceCount > 20000) throw new BusinessRuleException("EXCEL_TOO_LARGE", "单次最多读取 20000 行商品记录，请拆分后上传");
                    rows.Add(new Row(values[0], values[1], values[2], values[3], values[4], values[5], [$"{reader.Name}!{rowNumber}"]));
                    if (rows.Count > 5000) throw new BusinessRuleException("PRODUCT_LIMIT", "单次最多导入 5000 行商品");
                }
                if (columns is null) skipped.Add(reader.Name + "（未找到商品表头）");
            } while (reader.NextResult());
            if (rows.Count == 0) throw new BusinessRuleException("NO_PRODUCTS", "未识别到商品及现有库存，请使用“商品信息及现有库存模板.xlsx”，表头须包含“产品名称”和“现有库存”");
            return new Preview(rows, sourceCount, skipped);
        }
        catch (BusinessRuleException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or System.Xml.XmlException or ArgumentException or ExcelDataReader.Exceptions.ExcelReaderException)
        {
            throw new BusinessRuleException("INVALID_EXCEL", "无法读取此 Excel，请确认文件未加密、未损坏，并另存为 .xlsx 后重试");
        }
    }
    private static string Cell(object? value) => value switch {
        null => "", double number => number.ToString("G15", CultureInfo.InvariantCulture),
        _ => (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Trim()
    };
}
