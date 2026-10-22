using System.Windows;

namespace Estatisticas15BPM;
public partial class SummaryWindow : Window
{
    public SummaryWindow(string title,string summary,string finishLabel){InitializeComponent();TitleText.Text=title;SummaryText.Text=summary;FinishButton.Content=finishLabel;}
    private void Back_Click(object sender,RoutedEventArgs e)=>DialogResult=false;
    private void Finish_Click(object sender,RoutedEventArgs e)=>DialogResult=true;
}
