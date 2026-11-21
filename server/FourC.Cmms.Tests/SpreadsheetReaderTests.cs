using System.IO.Compression;
using System.Text;
using FourC.Cmms.Api.Features.Import;
using FourC.Cmms.Api.Infrastructure;
using FourC.Cmms.Core.Common;

namespace FourC.Cmms.Tests;

public class SpreadsheetReaderTests
{
    [Fact]
    public void Reads_what_the_export_writes()
    {
        var bytes = TableExport.Xlsx([
            new ExportSheet("Sites", [new ExportColumn("code", "Code"), new ExportColumn("name", "Name")], [["CAI", "Cairo"]]),
            new ExportSheet("Assets", [new ExportColumn("n", "Asset number"), new ExportColumn("p", "Price"), new ExportColumn("d", "Commissioned on"), new ExportColumn("x", "Notes")],
                [["PUMP-01", 12.5m, new DateOnly(2026, 11, 21), null]]),
        ]);
        var book = SpreadsheetReader.Read(new MemoryStream(bytes), "data.xlsx");

        Assert.Equal(["Sites", "Assets"], book.Sheets.Select(s => s.Name));
        var row = book.Sheets[1].Rows[1];
        Assert.Equal("PUMP-01", row[0]);
        Assert.Equal(12.5m, row[1]);
        Assert.Equal(new DateOnly(2026, 11, 21), row[2]);
        Assert.Null(row.ElementAtOrDefault(3));
        Assert.Equal("Asset number", book.Sheets[1].Rows[0][0]);
    }

    [Fact]
    public void Reads_an_excel_style_workbook()
    {
        // Laid out the way Excel saves: shared strings (one with rich-text runs), styles with a custom date format,
        // a boolean, a formula with its cached text, and a skipped cell reference.
        var bytes = Zip(new()
        {
            ["[Content_Types].xml"] = "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>",
            ["xl/workbook.xml"] = "<?xml version=\"1.0\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
                + "<sheets><sheet name=\"Assets\" sheetId=\"7\" r:id=\"rId3\"/></sheets></workbook>",
            ["xl/_rels/workbook.xml.rels"] = "<?xml version=\"1.0\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet9.xml\"/></Relationships>",
            ["xl/sharedStrings.xml"] = "<?xml version=\"1.0\"?><sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"3\" uniqueCount=\"3\">"
                + "<si><t>Name</t></si><si><r><t>Pu</t></r><r><rPr><b/></rPr><t>mp</t></r></si><si><t xml:space=\"preserve\"> Date </t></si></sst>",
            ["xl/styles.xml"] = "<?xml version=\"1.0\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">"
                + "<numFmts count=\"1\"><numFmt numFmtId=\"165\" formatCode=\"dd/mm/yyyy\"/></numFmts>"
                + "<cellXfs count=\"3\"><xf numFmtId=\"0\"/><xf numFmtId=\"165\" applyNumberFormat=\"1\"/><xf numFmtId=\"4\"/></cellXfs></styleSheet>",
            ["xl/worksheets/sheet9.xml"] = "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"
                + "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>2</v></c></row>"
                + "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>1</v></c><c r=\"C2\" s=\"1\"><v>46347</v></c><c r=\"D2\" t=\"b\"><v>1</v></c><c r=\"E2\" t=\"str\"><f>A1</f><v>X</v></c><c r=\"F2\" s=\"2\"><v>1234.5</v></c></row>"
                + "</sheetData></worksheet>",
        });
        var sheet = Assert.Single(SpreadsheetReader.Read(new MemoryStream(bytes), "book.xlsx").Sheets);

        Assert.Equal("Assets", sheet.Name);
        Assert.Equal(" Date ", sheet.Rows[0][1]);
        var row = sheet.Rows[1];
        Assert.Equal("Pump", row[0]);
        Assert.Null(row[1]);
        Assert.Equal(new DateOnly(2026, 11, 21), row[2]);
        Assert.Equal(true, row[3]);
        Assert.Equal("X", row[4]);
        Assert.Equal(1234.5m, row[5]);
    }

    [Fact]
    public void Reads_csv_with_comma_semicolon_and_bom()
    {
        var semi = SpreadsheetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("﻿Code;Name\nA1;\"x;y\"\n")), "Assets.csv");
        var sheet = Assert.Single(semi.Sheets);
        Assert.Equal("Assets", sheet.Name);
        Assert.Equal(2, sheet.Rows.Count);
        Assert.Equal(["Code", "Name"], sheet.Rows[0].Cast<string>());
        Assert.Equal(["A1", "x;y"], sheet.Rows[1].Cast<string>());

        var comma = SpreadsheetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("Code,Name\r\nB2,\"He said \"\"hi\"\", ok\"\r\n")), "sites-november.csv");
        Assert.Equal("sites-november", comma.Sheets[0].Name);
        Assert.Equal("He said \"hi\", ok", comma.Sheets[0].Rows[1][1]);
    }

    [Fact]
    public void Refuses_old_excel_files()
    {
        var e = Assert.Throws<DomainException>(() => SpreadsheetReader.Read(new MemoryStream([0xD0, 0xCF, 0x11, 0xE0]), "data.xls"));
        Assert.Equal("Save the file as .xlsx (Excel workbook) or .csv first.", e.Message);
        var broken = Assert.Throws<DomainException>(() => SpreadsheetReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("not a zip")), "data.xlsx"));
        Assert.Equal("This file can't be read as an Excel workbook.", broken.Message);
    }

    private static byte[] Zip(Dictionary<string, string> parts)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (path, xml) in parts)
            {
                using var w = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
        return ms.ToArray();
    }
}
