using System.ComponentModel;
using System.Windows;

namespace InternetFailover.Wpf
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow
  {
    private delegate void AddToLogProc(string message, params object[] parameters);

    public MainWindow()
    {
      InitializeComponent();
      IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
      if ((bool)e.NewValue && !LbLog.Items.IsEmpty)
        LbLog.ScrollIntoView(LbLog.Items[^1]);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
      e.Cancel = true;
      Hide();
    }

    public void AddToLog(string message, params object[] parameters)
    {
      LbLog.Dispatcher.BeginInvoke(new AddToLogProc(AddToLogFunc), message, parameters);
    }
    
    private void AddToLogFunc(string message, params object[] parameters)
    {
      LbLog.Items.Add(string.Format(message, parameters));
      LbLog.ScrollIntoView(LbLog.Items[^1]);
    }
  }
}