using System.Windows;
using System.Windows.Controls;

namespace Estatisticas15BPM;

public sealed class PasswordWindow : Window
{
    private readonly PasswordBox _password = new() { MinWidth = 280 };
    private readonly PasswordBox _confirmation = new() { MinWidth = 280 };
    public string? Password { get; private set; }

    public PasswordWindow()
    {
        Title = "Alterar senha"; Width = 390; Height = 235; WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Nova senha", FontWeight = FontWeights.SemiBold });
        panel.Children.Add(_password);
        panel.Children.Add(new TextBlock { Text = "Confirme a nova senha", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 3) });
        panel.Children.Add(_confirmation);
        var confirm = new Button { Content = "SALVAR NOVA SENHA", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 20, 0, 0) };
        confirm.Click += (_, _) => Confirm();
        panel.Children.Add(confirm);
        Content = panel;
    }

    private void Confirm()
    {
        if (_password.Password != _confirmation.Password) { MessageBox.Show("As senhas não conferem."); return; }
        if (_password.Password.Length < 6) { MessageBox.Show("A senha deve ter pelo menos 6 caracteres."); return; }
        Password = _password.Password;
        DialogResult = true;
    }
}
