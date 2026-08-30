using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using SmartNet.Exportacion.Infrastructure;

namespace SmartNet.Exportacion.Infrastructure.Tests;

/// <summary>
/// tasks.md PR1 task 1.1 / ADR 0021 decision 3: <see cref="ExportadorXlsx.Escribir"/> writes a real
/// <c>.xlsx</c> (an OOXML package) into the given stream, row at a time. Every cell is emitted as
/// inline text so that accounting identifiers keep their leading zeros (ADR 0021: a renamed CSV
/// "pierde los ceros a la izquierda", which a purchase catalog cannot afford). The bytes are
/// reopened with the first-party SDK to prove they are a valid workbook.
/// </summary>
public sealed class ExportadorXlsxTests
{
    private static List<List<string>> LeerHoja(byte[] contenido)
    {
        using var stream = new MemoryStream(contenido);
        using var documento = SpreadsheetDocument.Open(stream, isEditable: false);
        var worksheetPart = documento.WorkbookPart!.WorksheetParts.Single();

        var filas = new List<List<string>>();
        foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
        {
            var celdas = new List<string>();
            foreach (var cell in row.Elements<Cell>())
            {
                celdas.Add(cell.InlineString?.Text?.Text ?? cell.CellValue?.Text ?? string.Empty);
            }

            filas.Add(celdas);
        }

        return filas;
    }

    private static byte[] Escribir(IEnumerable<IReadOnlyList<string>> filas, IReadOnlyList<string> columnas)
    {
        using var buffer = new MemoryStream();
        ExportadorXlsx.Escribir(buffer, filas, columnas);
        return buffer.ToArray();
    }

    [Fact]
    public void Escribir_ConCabecerasYFilas_ProduceUnLibroLegibleConLosMismosValores()
    {
        var columnas = new[] { "Cuenta", "Descripcion", "Saldo", "Vigencia" };
        var filas = new IReadOnlyList<string>[]
        {
            new[] { "001", "Caja", "1234.50", "2026-08-30" },
            new[] { "421201", "Facturas por pagar", "-98.00", "2026-01-01" },
        };

        var contenido = Escribir(filas, columnas);
        var hoja = LeerHoja(contenido);

        Assert.Equal(3, hoja.Count); // 1 header + 2 data rows
        Assert.Equal(new[] { "Cuenta", "Descripcion", "Saldo", "Vigencia" }, hoja[0]);
        Assert.Equal(new[] { "001", "Caja", "1234.50", "2026-08-30" }, hoja[1]);
        Assert.Equal(new[] { "421201", "Facturas por pagar", "-98.00", "2026-01-01" }, hoja[2]);
    }

    [Fact]
    public void Escribir_SinFilas_ProduceUnLibroValidoConSoloLaCabecera()
    {
        var columnas = new[] { "Fecha", "Origen", "Compra", "Venta" };

        var contenido = Escribir(Array.Empty<IReadOnlyList<string>>(), columnas);
        var hoja = LeerHoja(contenido);

        Assert.Single(hoja);
        Assert.Equal(new[] { "Fecha", "Origen", "Compra", "Venta" }, hoja[0]);
    }

    [Fact]
    public void Escribir_SinColumnas_Rechaza()
    {
        using var buffer = new MemoryStream();
        Assert.Throws<ArgumentException>(
            () => ExportadorXlsx.Escribir(buffer, Array.Empty<IReadOnlyList<string>>(), Array.Empty<string>()));
    }
}
