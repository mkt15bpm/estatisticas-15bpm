using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Estatisticas15BPM;

/// <summary>Cria um PDF diretamente a partir da mesma página exibida na pré-visualização.</summary>
public static class VisualPdfWriter
{
    private const int A4WidthPixels = 1587;
    private const int A4HeightPixels = 2245;

    public static void Save(IEnumerable<Border> pages, string path)
    {
        var renderedPages = pages.Select(CreateA4Bitmap).ToList();
        if (renderedPages.Count == 0) throw new InvalidOperationException("Não há conteúdo para gerar o PDF.");
        WritePdf(path, renderedPages);
    }

    public static void Save(Border page, string path) => Save(new[] { page }, path);

    private static byte[] EncodeJpeg(BitmapSource bitmap)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 94 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var imageStream = new MemoryStream();
        encoder.Save(imageStream);
        return imageStream.ToArray();
    }

    public static BitmapSource CreateA4Bitmap(Border page)
    {
        page.UpdateLayout();
        var sourceWidth = Math.Max(1, page.ActualWidth);
        var sourceHeight = Math.Max(1, page.ActualHeight);
        const double margin = 96;
        var availableWidth = A4WidthPixels - (margin * 2);
        var availableHeight = A4HeightPixels - (margin * 2);
        var factor = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
        var width = sourceWidth * factor;
        var height = sourceHeight * factor;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle(Brushes.White, null, new Rect(0, 0, A4WidthPixels, A4HeightPixels));
            context.DrawRectangle(new VisualBrush(page) { Stretch = Stretch.Fill }, null, new Rect((A4WidthPixels - width) / 2, margin, width, height));
        }
        // As coordenadas do desenho estão em unidades WPF (96 DPI). Manter a
        // mesma escala evita que a metade direita/inferior da folha seja cortada.
        var bitmap = new RenderTargetBitmap(A4WidthPixels, A4HeightPixels, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;
    }

    private static void WritePdf(string path, IReadOnlyList<BitmapSource> pages)
    {
        const double pageWide = 595.28;
        const double pageHigh = 841.89;
        var objects = new List<byte[]>
        {
            B("<< /Type /Catalog /Pages 2 0 R >>"),
            B($"<< /Type /Pages /Kids [{string.Join(' ', Enumerable.Range(0, pages.Count).Select(index => $"{3 + (index * 3)} 0 R"))}] /Count {pages.Count} >>")
        };
        foreach (var bitmap in pages)
        {
            var pageObject = objects.Count + 1;
            var contentObject = pageObject + 1;
            var imageObject = pageObject + 2;
            var content = $"q\n{N(pageWide)} 0 0 {N(pageHigh)} 0 0 cm\n/Im0 Do\nQ";
            objects.Add(B($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(pageWide)} {N(pageHigh)}] /Resources << /XObject << /Im0 {imageObject} 0 R >> >> /Contents {contentObject} 0 R >>"));
            objects.Add(B($"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"));
            var image = EncodeJpeg(bitmap);
            objects.Add(imageStreamObject($"<< /Type /XObject /Subtype /Image /Width {A4WidthPixels} /Height {A4HeightPixels} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {image.Length} >>\nstream\n", image));
        }
        using var stream = File.Create(path);
        var offsets = new List<long> { 0 };
        Write(stream, "%PDF-1.4\n%\u00e2\u00e3\u00cf\u00d3\n");
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(stream.Position);
            Write(stream, $"{index + 1} 0 obj\n");
            stream.Write(objects[index]);
            Write(stream, "\nendobj\n");
        }
        var xref = stream.Position;
        Write(stream, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Write(stream, $"{offset:D10} 00000 n \n");
        Write(stream, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
    }

    private static byte[] imageStreamObject(string dictionary, byte[] image)
    {
        using var result = new MemoryStream();
        Write(result, dictionary);
        result.Write(image);
        Write(result, "\nendstream");
        return result.ToArray();
    }

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static byte[] B(string text) => Encoding.ASCII.GetBytes(text);
    private static void Write(Stream stream, string text) => stream.Write(B(text));
}
