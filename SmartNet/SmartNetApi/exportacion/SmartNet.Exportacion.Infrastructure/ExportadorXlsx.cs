using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SmartNet.Exportacion.Infrastructure;

/// <summary>
/// The single piece ADR 0021 decision 2 describes: "un escritor que recibe un <c>Stream</c>, una
/// secuencia de filas y una descripcion de columnas". It knows no accounting rule — exporting is a
/// read-only projection of a query result that already existed. Every value is written as inline
/// text so leading zeros on cuenta / RUC survive (ADR 0021 decision 1, the reason CSV was rejected).
///
/// ADR 0021 decision 3: an <c>.xlsx</c> is a ZIP package and needs a seekable stream, so the workbook
/// is built in a <see cref="MemoryStream"/> and copied to <paramref name="destino"/> once complete.
/// Rows are streamed one at a time via <see cref="OpenXmlWriter"/> — the whole book is never held as
/// an object tree — keeping the peak bounded (~5–10 MB for the ~6,600-row worst case).
/// </summary>
public static class ExportadorXlsx
{
    public static void Escribir(
        Stream destino,
        IEnumerable<IReadOnlyList<string>> filas,
        IReadOnlyList<string> columnas)
    {
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(filas);
        ArgumentNullException.ThrowIfNull(columnas);

        if (columnas.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos una columna.", nameof(columnas));
        }

        using var buffer = new MemoryStream();

        using (var documento = SpreadsheetDocument.Create(buffer, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = documento.AddWorkbookPart();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();

            using (var writer = OpenXmlWriter.Create(worksheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                writer.WriteStartElement(new SheetData());

                EscribirFila(writer, columnas);
                foreach (var fila in filas)
                {
                    EscribirFila(writer, fila);
                }

                writer.WriteEndElement(); // SheetData
                writer.WriteEndElement(); // Worksheet
            }

            using (var writer = OpenXmlWriter.Create(workbookPart))
            {
                writer.WriteStartElement(new Workbook());
                writer.WriteStartElement(new Sheets());
                writer.WriteElement(new Sheet
                {
                    Name = "Datos",
                    SheetId = 1U,
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                });
                writer.WriteEndElement(); // Sheets
                writer.WriteEndElement(); // Workbook
            }
        }

        buffer.Position = 0;
        buffer.CopyTo(destino);
    }

    private static void EscribirFila(OpenXmlWriter writer, IEnumerable<string> valores)
    {
        writer.WriteStartElement(new Row());
        foreach (var valor in valores)
        {
            writer.WriteStartElement(new Cell { DataType = CellValues.InlineString });
            writer.WriteStartElement(new InlineString());
            writer.WriteElement(new Text(valor ?? string.Empty));
            writer.WriteEndElement(); // InlineString
            writer.WriteEndElement(); // Cell
        }

        writer.WriteEndElement(); // Row
    }
}
