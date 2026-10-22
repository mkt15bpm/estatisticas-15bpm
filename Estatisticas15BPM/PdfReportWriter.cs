using System.Text;
using System.IO;
using System.Globalization;

namespace Estatisticas15BPM;

public static class PdfReportWriter
{
    public static void Save(string path, string title, string[] headers, IEnumerable<string[]> rows, long? totalTcosOverride = null)
    {
        var all = rows.ToList();
        const int perPage = 30;
        var pages = all.Chunk(perPage).ToList();
        if (pages.Count == 0) pages.Add(Array.Empty<string[]>());
        var objects = new List<byte[]>();
        objects.Add(B("<< /Type /Catalog /Pages 2 0 R >>"));
        var kids = string.Join(' ', Enumerable.Range(0, pages.Count).Select(i => $"{4 + i * 2} 0 R"));
        objects.Add(B($"<< /Type /Pages /Kids [{kids}] /Count {pages.Count} >>"));
        objects.Add(B("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
        for (var i = 0; i < pages.Count; i++)
        {
            var pageObject = 4 + i * 2;
            var contentObject = pageObject + 1;
            objects.Add(B($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {contentObject} 0 R >>"));
            var total = totalTcosOverride ?? (headers.Any(x => x.Contains("QUANTIDADE DE TCOs", StringComparison.OrdinalIgnoreCase)) ? all.Sum(row => long.TryParse(row.ElementAtOrDefault(1), out var q) ? q : 0) : 0);
            var content = Render(title, headers, pages[i], i + 1, pages.Count, total);
            var data = B(content);
            objects.Add(B($"<< /Length {data.Length} >>\nstream\n{content}\nendstream"));
        }
        using var stream = File.Create(path);
        var offsets = new List<long> { 0 };
        Write(stream, "%PDF-1.4\n%âãÏÓ\n");
        for (var i = 0; i < objects.Count; i++) { offsets.Add(stream.Position); Write(stream, $"{i + 1} 0 obj\n"); stream.Write(objects[i]); Write(stream, "\nendobj\n"); }
        var xref = stream.Position;
        Write(stream, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        for (var i = 1; i < offsets.Count; i++) Write(stream, $"{offsets[i]:D10} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }

    private static string Render(string title, string[] headers, string[][] rows, int page, int total, long totalTcos)
    {
        var sb = new StringBuilder();
        sb.Append("0.04 0.15 0.25 rg 40 790 515 24 re f\n1 1 1 rg BT /F1 14 Tf 52 798 Td (").Append(E(title)).Append(") Tj ET\n");
        if(totalTcos>0) Text(sb,40,770,$"TOTAL DE TCOs NO PERÍODO: {totalTcos}",10);
        sb.Append("0.92 0.94 0.95 rg 40 752 515 22 re f\n0.04 0.15 0.25 rg");
        var widths = Enumerable.Repeat(515.0 / headers.Length, headers.Length).ToArray(); var x = 40.0;
        for (var c = 0; c < headers.Length; c++) { sb.Append($"\n{F(x)} 752 m {F(x)} 82 l S"); Text(sb, x + 5, 760, headers[c], 9); x += widths[c]; }
        sb.Append($"\n555 752 m 555 82 l S\n40 752 m 555 752 l S\n40 730 m 555 730 l S");
        for (var r = 0; r < rows.Length; r++) { var y = 730 - r * 21; x = 40; for (var c = 0; c < headers.Length; c++) { Text(sb, x + 5, y - 14, rows[r].ElementAtOrDefault(c) ?? "", 8); x += widths[c]; } sb.Append($"\n40 {F(y - 21)} m 555 {F(y - 21)} l S"); }
        Text(sb, 40, 54, $"ESTATÍSTICAS 15ª BPM - Página {page} de {total}", 8);
        return sb.ToString();
    }
    private static void Text(StringBuilder sb, double x, double y, string value, int size) => sb.Append($"\nBT /F1 {size} Tf {F(x)} {F(y)} Td ({E(Truncate(value, 35))}) Tj ET");
    private static string F(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..Math.Max(1, length - 3)] + "...";
    private static string E(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
    private static byte[] B(string value) => Encoding.Latin1.GetBytes(value);
    private static void Write(Stream stream, string value) { var data = B(value); stream.Write(data); }
}
