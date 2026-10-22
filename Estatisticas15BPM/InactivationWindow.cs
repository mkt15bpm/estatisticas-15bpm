using System.Windows;
using System.Windows.Controls;

namespace Estatisticas15BPM;

public sealed class InactivationWindow : Window
{
    private readonly ComboBox _reason = new() { ItemsSource = new[] { "DUPLICIDADE DE REGISTRO", "ERRO DE CADASTRO", "REGISTRO INCORRETO", "OUTRO" }, SelectedIndex = 0, MinWidth = 330 };
    private readonly TextBox _other = new() { MinWidth = 330, Visibility = Visibility.Collapsed, MaxLength = 300 };
    public string? Reason { get; private set; }

    public InactivationWindow()
    {
        Title = "Inativar registro"; Width = 440; Height = 245; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Motivo da inativação", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_reason);
        panel.Children.Add(new TextBlock { Text = "Descreva o motivo", Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(_other);
        var confirm = new Button { Content = "CONFIRMAR INATIVAÇÃO", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
        confirm.Click += (_, _) => Confirm();
        panel.Children.Add(confirm);
        _reason.SelectionChanged += (_, _) => _other.Visibility = (string?)_reason.SelectedItem == "OUTRO" ? Visibility.Visible : Visibility.Collapsed;
        Content = panel;
    }

    private void Confirm()
    {
        Reason = (string?)_reason.SelectedItem == "OUTRO" ? _other.Text.Trim() : (string?)_reason.SelectedItem;
        if (string.IsNullOrWhiteSpace(Reason)) { MessageBox.Show("Informe o motivo da inativação."); return; }
        DialogResult = true;
    }
}
