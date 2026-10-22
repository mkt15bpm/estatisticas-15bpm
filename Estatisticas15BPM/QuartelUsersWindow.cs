using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Estatisticas15BPM;

public sealed class QuartelUsersWindow : Window
{
    public QuartelUsersWindow(string quartel, List<UsuarioResumo> users)
    {
        Title = "Operadores do quartel";
        Width = 760;
        Height = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"OPERADORES — {quartel}", FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
        var grid = new DataGrid { ItemsSource = users, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false, Height = 360 };
        grid.Columns.Add(new DataGridTextColumn { Header = "Nome", Binding = new Binding("Nome"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "Posto", Binding = new Binding("Posto"), Width = 130 });
        grid.Columns.Add(new DataGridTextColumn { Header = "RGPM", Binding = new Binding("Login"), Width = 110 });
        grid.Columns.Add(new DataGridTextColumn { Header = "Situação", Binding = new Binding("Situacao"), Width = 100 });
        panel.Children.Add(grid);
        Content = panel;
    }
}
