using System.Windows;
using System.IO;
using System.Linq;

namespace Estatisticas15BPM;
public partial class DatabasePickerWindow : Window
{
    public string? SelectedDatabase { get; private set; }
    public DatabasePickerWindow(IEnumerable<string> databases) { InitializeComponent(); DatabaseList.ItemsSource = databases.Select(x => new DatabaseOption(x)); DatabaseList.SelectedIndex = 0; }
    private void Use_Click(object sender, RoutedEventArgs e) { SelectedDatabase = (DatabaseList.SelectedItem as DatabaseOption)?.Path; DialogResult = SelectedDatabase is not null; }
    private sealed record DatabaseOption(string Path) { public string DisplayName => $"{System.IO.Path.GetFileName(Path)}  —  {File.GetCreationTime(Path):dd/MM/yyyy}"; }
}
