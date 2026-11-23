namespace ERP.Application.Common.Interfaces;

public sealed record XlsxRow(int RowNumber, IReadOnlyList<string> Cells);

/// <summary>İlk sayfa: Headers ilk satır, Rows boş olmayan veri satırları (Cells başlıklarla aynı sırada, metin olarak).</summary>
public sealed record XlsxTable(IReadOnlyList<string> Headers, IReadOnlyList<XlsxRow> Rows);

/// <summary>.xlsx okuma ve yazma (ClosedXML, Infrastructure).</summary>
public interface IXlsxService {
    /// <summary>Değerler string (metin hücre), sayı, bool veya null olabilir.</summary>
    byte[] Write(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows);

    /// <summary>En fazla maxRows + 1 veri satırı okur (sınır aşımı çağıranca anlaşılsın). Geçersiz dosyada <see cref="InvalidDataException" />.</summary>
    XlsxTable Read(Stream stream, int maxRows);
}
