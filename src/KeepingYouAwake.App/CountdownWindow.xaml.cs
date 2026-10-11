using System.ComponentModel;
using System.Windows;

namespace KeepingYouAwake.App;
public partial class CountdownWindow : Window
{
    public bool AllowClose { get; set; }
    public CountdownWindow()
    {
        InitializeComponent();
        Closing += (_, e) =>
        {
            if (!AllowClose && !App.CurrentApp.Quitting)
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(() => App.CurrentApp.Run(new("cancel")));
            }
        };
    }
    public void Update(int remaining) => Seconds.Text = remaining.ToString();
    private void CancelClick(object sender, RoutedEventArgs e) => App.CurrentApp.Run(new("cancel"));
}
