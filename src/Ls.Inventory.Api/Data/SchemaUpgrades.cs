using Microsoft.EntityFrameworkCore;
namespace Ls.Inventory.Api.Data;

public static class SchemaUpgrades
{
    // Validate under table locks before conversion: never round or discard existing quantities.
    public static async Task ApplyAsync(InventoryDbContext db)
    {
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(82461002)");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE products ADD COLUMN IF NOT EXISTS warning_quantity numeric(20,6) NULL");
        await db.Database.ExecuteSqlRawAsync("""
            DO $$ BEGIN
              IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'ck_product_warning_quantity' AND conrelid = 'products'::regclass) THEN
                ALTER TABLE products ADD CONSTRAINT ck_product_warning_quantity CHECK (warning_quantity IS NULL OR warning_quantity >= 0);
              END IF;
            END $$;
            """);
        var needsConversion = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::int AS "Value" FROM pg_attribute
            WHERE (attrelid = 'products'::regclass AND attname IN ('quantity','warning_quantity')
              OR attrelid = 'movements'::regclass AND attname IN ('quantity_change','quantity_after')
              OR attrelid = 'stocktake_lines'::regclass AND attname IN ('expected_quantity','counted_quantity'))
              AND atttypid <> 'integer'::regtype AND NOT attisdropped
            """).SingleAsync();
        if (needsConversion > 0) {
            await db.Database.ExecuteSqlRawAsync("LOCK TABLE products, movements, stocktake_lines IN ACCESS EXCLUSIVE MODE");
            await ValidateIntegerQuantitiesAsync(db);
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE products ALTER COLUMN quantity TYPE integer USING quantity::integer,
                  ALTER COLUMN warning_quantity TYPE integer USING warning_quantity::integer;
                ALTER TABLE movements ALTER COLUMN quantity_change TYPE integer USING quantity_change::integer,
                  ALTER COLUMN quantity_after TYPE integer USING quantity_after::integer;
                ALTER TABLE stocktake_lines ALTER COLUMN expected_quantity TYPE integer USING expected_quantity::integer,
                  ALTER COLUMN counted_quantity TYPE integer USING counted_quantity::integer;
                """);
        }
        await transaction.CommitAsync();
    }

    public static async Task ValidateIntegerQuantitiesAsync(InventoryDbContext db)
    {
        var invalid = await db.Database.SqlQueryRaw<long>("""
            SELECT count(*) AS "Value" FROM (
              SELECT quantity AS value, 0 AS minimum FROM products
              UNION ALL SELECT warning_quantity, 0 FROM products
              UNION ALL SELECT quantity_after, 0 FROM movements
              UNION ALL SELECT quantity_change, -2147483648 FROM movements
              UNION ALL SELECT expected_quantity, 0 FROM stocktake_lines
              UNION ALL SELECT counted_quantity, 0 FROM stocktake_lines
            ) quantities WHERE value IS NOT NULL
              AND (value <> trunc(value) OR value < minimum OR value > 2147483647)
            """).SingleAsync();
        if (invalid > 0) throw new InvalidOperationException($"发现 {invalid} 个数量字段包含小数或超出 int 范围，整数升级已停止；请先核实数据，系统不会自动取整。");
    }
}
