using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Estatisticas15BPM;

public sealed class ReportPreviewWindow : Window
{
    private readonly Border _page;
    private readonly string _report;
    private readonly string[]? _headers;
    private readonly List<string[]>? _rows;
    private readonly ImageSource? _chart;

    public ReportPreviewWindow(string report, string[]? headers = null, IEnumerable<string[]>? rows = null, ImageSource? chart = null)
    {
        _report = report;
        _headers = headers;
        _rows = rows is null || headers is null ? null : PrepareRows(report, headers, rows.ToList());
        _chart = chart;
        Title = "Pré-visualização do relatório";
        Width = 850;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Background = new SolidColorBrush(Color.FromRgb(235, 238, 240)) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 12, 18, 12), HorizontalAlignment = HorizontalAlignment.Right };
        var print = new Button { Content = "IMPRIMIR", Background = new SolidColorBrush(Color.FromRgb(161, 125, 62)), Foreground = Brushes.White, Padding = new Thickness(20, 10, 20, 10) };
        print.Click += (_, _) => Print();
        var pdf = new Button { Content = "GERAR PDF", Background = new SolidColorBrush(Color.FromRgb(73, 101, 120)), Foreground = Brushes.White, Padding = new Thickness(20, 10, 20, 10) };
        pdf.Click += (_, _) => GeneratePdf();
        var close = new Button { Content = "FECHAR", Background = new SolidColorBrush(Color.FromRgb(73, 101, 120)), Foreground = Brushes.White, Padding = new Thickness(20, 10, 20, 10) };
        close.Click += (_, _) => Close();
        actions.Children.Add(print); actions.Children.Add(pdf); actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
        _page = new Border { Background = Brushes.White, Padding = new Thickness(55), Margin = new Thickness(38, 24, 38, 12), BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 220)), BorderThickness = new Thickness(1), Child = CreatePreviewDocument() };
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _page });
        Content = root;
    }

    private StackPanel CreatePreviewDocument()
    {
        var document = new StackPanel();
        document.Children.Add(CreateCrest(92, new Thickness(0, 0, 0, 14)));
        document.Children.Add(new TextBlock { Text = _report, FontFamily = new FontFamily("Segoe UI"), FontSize = 14, Foreground = Navy, TextWrapping = TextWrapping.Wrap, LineHeight = 22, Margin = new Thickness(0, 0, 0, 14) });
        if (_chart is not null) document.Children.Add(new Image { Source = _chart, Height = 225, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) });
        if (_headers is not null && _rows is not null)
        {
            if (IsRanking()) document.Children.Add(new TextBlock { Text = $"TOTAL DE TCOs NO PERÍODO: {ResolveTotalTcos(_report) ?? SumTcos()}", FontWeight = FontWeights.SemiBold, Foreground = Navy, Margin = new Thickness(0, 0, 0, 10) });
            var table = new DataGrid { ItemsSource = _rows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, HeadersVisibility = DataGridHeadersVisibility.Column, Height = Math.Min(420, Math.Max(105, _rows.Count * 42 + 42)), Margin = new Thickness(0, 6, 0, 0) };
            for (var i = 0; i < _headers.Length; i++) table.Columns.Add(new DataGridTextColumn { Header = _headers[i], Binding = new System.Windows.Data.Binding($"[{i}]"), Width = i == _headers.Length - 1 ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(i == 0 ? 78 : 145) });
            document.Children.Add(table);
        }
        return document;
    }

    private void Print()
    {
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
            foreach (var printable in CreatePrintablePages())
            {
                var fixedPage = new FixedPage { Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight };
                fixedPage.Children.Add(new Image { Source = VisualPdfWriter.CreateA4Bitmap(printable), Width = dialog.PrintableAreaWidth, Height = dialog.PrintableAreaHeight, Stretch = Stretch.Uniform });
                var content = new PageContent(); ((IAddChild)content).AddChild(fixedPage); document.Pages.Add(content);
            }
            dialog.PrintDocument(document.DocumentPaginator, "Relatório ESTATÍSTICAS 15ª BPM");
        }
        catch (Exception ex) { MessageBox.Show($"Não foi possível enviar o relatório para impressão.\n\n{ex.Message}", "Impressão", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void GeneratePdf()
    {
        var dialog = new SaveFileDialog { Title = "Salvar relatório em PDF", Filter = "Arquivo PDF (*.pdf)|*.pdf", DefaultExt = ".pdf", FileName = $"{(_report.Contains("DASHBOARD", StringComparison.OrdinalIgnoreCase) ? "Dashboard" : _report.Contains("RANKING", StringComparison.OrdinalIgnoreCase) ? "Ranking" : "Estatisticas_15BPM")}_{DateTime.Now:dd-MM-yyyy}.pdf" };
        if (dialog.ShowDialog() != true) return;
        try { VisualPdfWriter.Save(CreatePrintablePages(), dialog.FileName); MessageBox.Show("PDF gerado com sucesso.", "Relatório em PDF", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show($"Não foi possível gerar o PDF.\n\n{ex.Message}", "Relatório em PDF", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private List<Border> CreatePrintablePages()
    {
        if (_headers is null || _rows is null) return new List<Border> { _page };
        var chunks = new List<List<string[]>>();
        for (var index = 0; index < _rows.Count;)
        {
            var take = chunks.Count == 0 && _chart is not null ? 10 : 18;
            chunks.Add(_rows.Skip(index).Take(take).ToList()); index += take;
        }
        if (chunks.Count == 0) chunks.Add(new List<string[]>());
        var totalTcos = IsRanking() ? ResolveTotalTcos(_report) ?? SumTcos() : 0;
        return chunks.Select((rows, i) => CreatePrintPage(rows, i + 1, chunks.Count, totalTcos)).ToList();
    }

    private Border CreatePrintPage(IReadOnlyList<string[]> rows, int pageNumber, int pageCount, long totalTcos)
    {
        var document = new StackPanel { Width = 650 };
        document.Children.Add(CreateCrest(78, new Thickness(0, 0, 0, 8)));
        document.Children.Add(new TextBlock { Text = _report, FontFamily = new FontFamily("Segoe UI"), FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Navy, TextWrapping = TextWrapping.Wrap, LineHeight = 18, Margin = new Thickness(0, 0, 0, 10) });
        if (pageNumber == 1 && _chart is not null) document.Children.Add(new Image { Source = _chart, Height = 175, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) });
        if (IsRanking()) document.Children.Add(new TextBlock { Text = $"TOTAL DE TCOs NO PERÍODO: {totalTcos}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Navy, Margin = new Thickness(0, 0, 0, 8) });
        document.Children.Add(CreatePrintTable(rows));
        document.Children.Add(new TextBlock { Text = $"ESTATÍSTICAS 15ª BPM  •  Página {pageNumber} de {pageCount}", HorizontalAlignment = HorizontalAlignment.Center, FontSize = 9, Foreground = Brushes.DimGray, Margin = new Thickness(0, 14, 0, 0) });
        var page = new Border { Width = 714, Background = Brushes.White, Padding = new Thickness(32), Child = document };
        page.Measure(new Size(page.Width, double.PositiveInfinity)); page.Arrange(new Rect(0, 0, page.Width, page.DesiredSize.Height));
        return page;
    }

    private Grid CreatePrintTable(IReadOnlyList<string[]> rows)
    {
        var table = new Grid { Width = 650 };
        foreach (var width in ColumnWidths()) table.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) });
        for (var row = 0; row < rows.Count; row++) table.RowDefinitions.Add(new RowDefinition { Height = new GridLength(30) });
        for (var column = 0; column < _headers!.Length; column++) AddTableCell(table, _headers[column], 0, column, true);
        for (var row = 0; row < rows.Count; row++) for (var column = 0; column < _headers!.Length; column++) AddTableCell(table, rows[row].ElementAtOrDefault(column) ?? "", row + 1, column, false);
        return table;
    }

    private IEnumerable<GridLength> ColumnWidths()
    {
        if (_headers!.Length == 3) return new[] { new GridLength(76), new GridLength(144), new GridLength(1, GridUnitType.Star) };
        if (_headers.Length == 5) return new[] { new GridLength(82), new GridLength(130), new GridLength(82), new GridLength(62), new GridLength(1, GridUnitType.Star) };
        return Enumerable.Repeat(new GridLength(1, GridUnitType.Star), _headers.Length);
    }

    private static void AddTableCell(Grid table, string text, int row, int column, bool header)
    {
        var border = new Border { BorderBrush = new SolidColorBrush(Color.FromRgb(205, 213, 219)), BorderThickness = new Thickness(0.5), Background = header ? Navy : row % 2 == 0 ? new SolidColorBrush(Color.FromRgb(244, 247, 249)) : Brushes.White, Padding = new Thickness(6, 3, 6, 3) };
        border.Child = new TextBlock { Text = text, Foreground = header ? Brushes.White : new SolidColorBrush(Color.FromRgb(25, 40, 53)), FontSize = header ? 8 : 8.5, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(border, row); Grid.SetColumn(border, column); table.Children.Add(border);
    }

    private long SumTcos() => _rows?.Sum(x => long.TryParse(x.ElementAtOrDefault(1), out var q) ? q : 0) ?? 0;
    private static Image CreateCrest(double height, Thickness margin) => new() { Source = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/15 BPM.png")), Height = height, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Margin = margin };
    private static readonly Brush Navy = new SolidColorBrush(Color.FromRgb(10, 37, 64));
    private bool IsRanking() => _headers?.Any(x => x.Contains("QUANTIDADE DE TCOs", StringComparison.OrdinalIgnoreCase)) == true;

    private static long? ResolveTotalTcos(string report)
    {
        try
        {
            var dates = Regex.Match(report, @"Período:\s*(\d{2}/\d{2}/\d{4})\s*a\s*(\d{2}/\d{2}/\d{4})"); if (!dates.Success) return null;
            var quartel = Regex.Match(report, @"Quartel:\s*(.+)").Groups[1].Value.Trim(); var tipo = Regex.Match(report, @"Tipo:\s*(.+)").Groups[1].Value.Trim();
            return new DatabaseService().GetEstatisticas(DateTime.Parse(dates.Groups[1].Value), DateTime.Parse(dates.Groups[2].Value), quartel == "TODOS" ? null : quartel, string.IsNullOrWhiteSpace(tipo) ? "TCO" : tipo).Tcos;
        }
        catch { return null; }
    }

    private static List<string[]> PrepareRows(string report, string[] headers, List<string[]> rows)
    {
        if (!headers.Any(x => x.Contains("QUANTIDADE DE TCOs", StringComparison.OrdinalIgnoreCase))) return rows;
        try
        {
            var quartel = Regex.Match(report, @"Quartel:\s*(.+)").Groups[1].Value.Trim(); var policiais = new DatabaseService().GetPoliciais(quartel == "TODOS" ? null : quartel);
            var postos = new Dictionary<string, int> { ["CORONEL"] = 1, ["TEN. CORONEL"] = 2, ["MAJOR"] = 3, ["CAPITÃO"] = 4, ["SUBTENENTE"] = 7, ["1º SARGENTO"] = 8, ["2º SARGENTO"] = 9, ["3º SARGENTO"] = 10, ["CABO"] = 11, ["SOLDADO"] = 12 };
            var nomes = policiais.ToDictionary(x => $"{x.Posto} PM {x.Qra}", x => x);
            return rows.Where(x => nomes.ContainsKey(x.ElementAtOrDefault(2) ?? "")).OrderByDescending(x => long.TryParse(x.ElementAtOrDefault(1), out var q) ? q : 0).ThenBy(x => { var q = long.TryParse(x.ElementAtOrDefault(1), out var n) ? n : 0; return q == 0 ? postos.GetValueOrDefault(nomes[x[2]].Posto, 99) : 0; }).ThenBy(x => { var q = long.TryParse(x.ElementAtOrDefault(1), out var n) ? n : 0; return q == 0 ? nomes[x[2]].Qra : x[2]; }).Select((x, i) => new[] { $"{i + 1}°", x.ElementAtOrDefault(1) ?? "0", x.ElementAtOrDefault(2) ?? "" }).ToList();
        }
        catch { return rows; }
    }
}
