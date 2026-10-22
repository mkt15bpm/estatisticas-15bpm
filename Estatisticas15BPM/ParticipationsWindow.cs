using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Estatisticas15BPM;

public sealed class ParticipationsWindow : Window
{
    public ParticipationsWindow(Policial policial, List<ParticipacaoResumo> items)
    {
        Title = "Participações do operador";
        Width = 980;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock { Text = $"REGISTROS DE PARTICIPAÇÃO — {policial.Posto} PM {policial.Qra}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var grid = new DataGrid { ItemsSource = items, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 370 };
        grid.Columns.Add(Column("Tipo", "Tipo", 130));
        grid.Columns.Add(Column("Nº TCO / B.O.", "Numero", 140));
        grid.Columns.Add(Column("Data do fato", "DataFato", 115));
        grid.Columns.Add(Column("Data do cadastro", "DataCadastro", 190));
        grid.Columns.Add(Column("Operador que cadastrou", "OperadorCadastro", new DataGridLength(1, DataGridLengthUnitType.Star)));
        root.Children.Add(grid);
        var print = new Button { Content = "PRÉ-VISUALIZAR E IMPRIMIR", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        print.Click += (_, _) => new ReportPreviewWindow($"ESTATÍSTICAS 15ª BPM\nParticipações de {policial.Posto} PM {policial.Qra}", new[] { "TIPO", "Nº TCO / B.O.", "DATA DO FATO", "DATA DO CADASTRO", "OPERADOR QUE CADASTROU" }, items.Select(x => new[] { x.Tipo, x.Numero, x.DataFato, x.DataCadastro, x.OperadorCadastro })) { Owner = this }.ShowDialog();
        root.Children.Add(print);
        Content = root;
    }

    private static DataGridTextColumn Column(string header, string path, object width) => new() { Header = header, Binding = new Binding(path), Width = width is DataGridLength l ? l : new DataGridLength((int)width) };
}
