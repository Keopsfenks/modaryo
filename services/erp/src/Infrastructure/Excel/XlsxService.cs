using System.Globalization;
using ClosedXML.Excel;
using ERP.Application.Common.Interfaces;

namespace ERP.Infrastructure.Excel;

public sealed class XlsxService : IXlsxService {
    public byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows) {
        using var workbook = new XLWorkbook();
        var       sheet    = workbook.AddWorksheet(sheetName);

        for (var c = 0; c < headers.Count; c++) {
            var cell = sheet.Cell(1, c + 1);
            cell.Value               = headers[c];
            cell.Style.Font.Bold     = true;
        }

        var r = 2;
        foreach (var row in rows) {
            for (var c = 0; c < row.Count; c++) {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c]) {
                    case null:
                        break;
                    case string text:
                        cell.SetValue(text); // metin olarak kalır: "00123" kodu sayıya dönüşmez
                        break;
                    case bool flag:
                        cell.SetValue(flag);
                        break;
                    case decimal number:
                        cell.SetValue((double)number);
                        break;
                    case int number:
                        cell.SetValue(number);
                        break;
                    default:
                        cell.SetValue(Convert.ToString(row[c], CultureInfo.InvariantCulture) ?? string.Empty);
                        break;
                }
            }

            r++;
        }

        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(r, 200));

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public XlsxTable Read(Stream stream, int maxRows) {
        try {
            using var workbook = new XLWorkbook(stream);
            var       sheet    = workbook.Worksheets.FirstOrDefault() ?? throw new InvalidDataException("Sayfa yok.");
            var       lastRow  = sheet.LastRowUsed()?.RowNumber() ?? 0;
            var       lastCol  = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (lastRow == 0 || lastCol == 0) return new XlsxTable([], []);

            var headers = Enumerable.Range(1, lastCol).Select(c => Text(sheet.Cell(1, c))).ToList();
            var rows    = new List<XlsxRow>();
            for (var r = 2; r <= lastRow && rows.Count <= maxRows; r++) {
                var cells = Enumerable.Range(1, lastCol).Select(c => Text(sheet.Cell(r, c))).ToList();
                if (cells.All(string.IsNullOrWhiteSpace)) continue;

                rows.Add(new XlsxRow(r, cells));
            }

            return new XlsxTable(headers, rows);
        } catch (InvalidDataException) {
            throw;
        } catch (Exception ex) {
            throw new InvalidDataException("Dosya geçerli bir .xlsx değil.", ex);
        }
    }

    private static string Text(IXLCell cell) {
        var value = cell.Value;
        return value.Type switch {
                   XLDataType.Blank    => string.Empty,
                   XLDataType.Text     => value.GetText().Trim(),
                   XLDataType.Boolean  => value.GetBoolean() ? "Evet" : "Hayır",
                   XLDataType.Number   => FormatNumber(value.GetNumber()),
                   XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                   XLDataType.TimeSpan => value.GetTimeSpan().ToString(),
                   _                   => value.ToString(CultureInfo.InvariantCulture).Trim()
               };
    }

    private static string FormatNumber(double number) {
        if (Math.Abs(number) < 1e15 && number == Math.Floor(number)) return ((long)number).ToString(CultureInfo.InvariantCulture);
        return Math.Abs(number) < 1e15
                   ? ((decimal)number).ToString(CultureInfo.InvariantCulture)
                   : number.ToString("R", CultureInfo.InvariantCulture);
    }
}
