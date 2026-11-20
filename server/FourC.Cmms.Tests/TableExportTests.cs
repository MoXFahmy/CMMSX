using System.IO.Compression;
using System.Text;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Work;

namespace FourC.Cmms.Tests;

public class TableExportTests
{
    public sealed record Row(string Number, WorkOrderStatus Status, WorkPriority? Priority, DateOnly? Due, DateTimeOffset At, List<string> Names,
        Dictionary<string, string?> CustomFields, bool Flag, decimal Cost, string? Note, Guid Id, Guid SiteId);

    private static readonly TimeZoneInfo Plus2 = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");

    private static Row Sample(string number = "WO-1", string? note = null) => new(number, WorkOrderStatus.InProgress, WorkPriority.High, new DateOnly(2026, 11, 30),
        new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), ["Ali", "Mona"], new() { ["colour"] = "Blue" }, true, 12.5m, note, Guid.NewGuid(), Guid.NewGuid());

    private static string Text(byte[] bytes) => Encoding.UTF8.GetString(bytes);

    [Fact]
    public void Csv_quotes_and_neutralises_formulas()
    {
        var cols = new[] { new ExportColumn("a", "A"), new ExportColumn("b", "B") };
        var csv = TableExport.Csv(cols, [["a,b", "say \"hi\""], ["=1+1", null], ["@cmd", "-2"]]);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, csv[..3]);
        var text = Encoding.UTF8.GetString(csv[3..]);
        Assert.Equal("A,B\r\n\"a,b\",\"say \"\"hi\"\"\"\r\n'=1+1,\r\n'@cmd,'-2\r\n", text);
    }

    [Fact]
    public void Csv_writes_dates_numbers_and_date_times_plainly()
    {
        var cols = new[] { new ExportColumn("d", "D"), new ExportColumn("t", "T"), new ExportColumn("n", "N") };
        var csv = Text(TableExport.Csv(cols, [[new DateOnly(2026, 11, 30), new DateTime(2026, 11, 19, 12, 5, 0), 12.5m]]));
        Assert.EndsWith("D,T,N\r\n2026-11-30,2026-11-19 12:05,12.5\r\n", csv);
    }

    [Fact]
    public void Rows_reads_paths_enums_dates_and_lists()
    {
        var cols = new[]
        {
            new ExportColumn("number", "No"), new ExportColumn("status", "Status"), new ExportColumn("priority", "Priority"), new ExportColumn("due", "Due"),
            new ExportColumn("at", "At"), new ExportColumn("names", "Names"), new ExportColumn("customFields.colour", "Colour"), new ExportColumn("flag", "Flag"),
            new ExportColumn("cost", "Cost"), new ExportColumn("note", "Note"), new ExportColumn("customFields.missing", "Missing"),
        };
        var row = TableExport.Rows([Sample()], cols, Plus2).Single();
        Assert.Equal("WO-1", row[0]);
        Assert.Equal("In progress", row[1]);
        Assert.Equal("High", row[2]);
        Assert.Equal(new DateOnly(2026, 11, 30), row[3]);
        Assert.Equal(new DateTime(2026, 11, 19, 12, 0, 0), row[4]);
        Assert.Equal("Ali, Mona", row[5]);
        Assert.Equal("Blue", row[6]);
        Assert.Equal("Yes", row[7]);
        Assert.Equal(12.5m, row[8]);
        Assert.Null(row[9]);
        Assert.Null(row[10]);
    }

    [Theory]
    [InlineData("number.chars")]
    [InlineData("number.length")]
    [InlineData("names.item")]
    [InlineData("status.x")]
    [InlineData("customFields.colour.more")]
    public void Rows_rejects_keys_that_reach_into_plain_values(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => TableExport.Rows([Sample()], [new ExportColumn(key, "X")], Plus2));
        Assert.Contains(key, ex.Message);
        Assert.Throws<ArgumentException>(() => TableExport.Check(typeof(Row), [new ExportColumn(key, "X")]));
    }

    [Fact]
    public void Xlsx_writes_dates_excel_cannot_hold_as_text()
    {
        var cols = new[] { new ExportColumn("a", "A"), new ExportColumn("b", "B"), new ExportColumn("c", "C") };
        var bytes = TableExport.Xlsx("Old", cols, [[new DateOnly(50, 6, 1), new DateOnly(1899, 12, 31), new DateTime(1900, 2, 1, 8, 0, 0)]]);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var r = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open());
        var sheet = r.ReadToEnd();
        Assert.Contains(">0050-06-01<", sheet);
        Assert.Contains(">1899-12-31<", sheet);
        Assert.Contains(">1900-02-01 08:00<", sheet);
        Assert.DoesNotContain("s=\"2\"", sheet);
    }

    [Fact]
    public void Rows_reads_anonymous_objects()
    {
        object item = new { Number = "PO-7", Status = WorkOrderStatus.OnHold, Total = 99.95m };
        var row = TableExport.Rows([item], [new ExportColumn("number", "N"), new ExportColumn("status", "S"), new ExportColumn("total", "T")], TimeZoneInfo.Utc).Single();
        Assert.Equal(["PO-7", "On hold", 99.95m], row);
    }

    [Fact]
    public void Rows_rejects_an_unknown_key()
    {
        var ex = Assert.Throws<ArgumentException>(() => TableExport.Rows([Sample()], [new ExportColumn("nope", "Nope")], Plus2));
        Assert.Contains("nope", ex.Message);
    }

    [Fact]
    public void Default_columns_leave_out_ids_and_dictionaries()
    {
        var keys = TableExport.DefaultColumns(typeof(Row)).Select(c => c.Key).ToList();
        Assert.Equal(["number", "status", "priority", "due", "at", "names", "flag", "cost", "note"], keys);
    }

    [Theory]
    [InlineData("InProgress", "In progress")]
    [InlineData("OnHold", "On hold")]
    [InlineData("High", "High")]
    [InlineData("PM", "PM")]
    [InlineData("PartiallyReceived", "Partially received")]
    public void Words_splits_pascal_case(string value, string expected) => Assert.Equal(expected, TableExport.Words(value));

    [Fact]
    public void Xlsx_is_a_valid_workbook()
    {
        var cols = new[] { new ExportColumn("number", "WO number"), new ExportColumn("cost", "Cost"), new ExportColumn("due", "Due"), new ExportColumn("at", "At"), new ExportColumn("note", "Note") };
        var rows = TableExport.Rows([Sample(note: "a < b & \u0001c"), Sample("WO-2")], cols, Plus2);
        var bytes = TableExport.Xlsx("Work orders", cols, rows);
        using var zip = new ZipArchive(new MemoryStream(bytes));
        string Part(string name) { using var r = new StreamReader(zip.GetEntry(name)!.Open()); return r.ReadToEnd(); }
        Assert.Contains("worksheets/sheet1.xml", Part("[Content_Types].xml"));
        Assert.Contains("name=\"Work orders\"", Part("xl/workbook.xml"));
        Assert.Contains("yyyy-mm-dd hh:mm", Part("xl/styles.xml"));
        var sheet = Part("xl/worksheets/sheet1.xml");
        Assert.Contains("<t>WO number</t>", sheet);
        Assert.Contains("<v>12.5</v>", sheet);
        Assert.Contains($"<c r=\"C2\" s=\"2\"><v>{new DateTime(2026, 11, 30).ToOADate()}</v></c>", sheet);
        Assert.Contains("<c r=\"D2\" s=\"3\">", sheet);
        Assert.Contains("a &lt; b &amp; c", sheet);
        Assert.Contains("<autoFilter ref=\"A1:E3\"/>", sheet);
        Assert.Contains("state=\"frozen\"", sheet);
        Assert.Contains("<c r=\"A1\" s=\"1\" t=\"inlineStr\">", sheet);
    }
}
