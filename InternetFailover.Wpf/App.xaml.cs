using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;

namespace InternetFailover.Wpf
{
  /// <summary>
  /// Interaction logic for App.xaml
  /// </summary>
  public partial class App
  {
    private class InternetFailoverWpf : InternetFailover.Core.InternetFailover
    {
      private readonly MainWindow _mw;
      
      public InternetFailoverWpf(MainWindow mw)
      {
        _mw = mw;
      }
      protected override void Log(string message, params object[] parameters)
      {
        _mw.AddToLog(message, parameters);
      }
    }
    
    private TaskbarIcon? _notifyIcon;
    private InternetFailoverWpf? _failoverHandler;
    private ImageSource? _green, _yellow;
    
    protected override void OnStartup(StartupEventArgs e)
    {
      base.OnStartup(e);
      _notifyIcon = (TaskbarIcon?) FindResource("NotifyIcon");
      _notifyIcon!.LeftClickCommand = ShowLogCommand;
      _green = new BitmapImage(new Uri("pack://application:,,,/green.ico"));
      _yellow = new BitmapImage(new Uri("pack://application:,,,/yellow.ico"));
      var w = new MainWindow();
      MainWindow = w;
      try
      {
        _failoverHandler = new InternetFailoverWpf(w);
        _failoverHandler.LogConfiguration();
        _failoverHandler.StateChanged += FailoverHandlerOnStateChanged;
        var t = new Thread(() =>
        {
          _failoverHandler.Prepare();
          _failoverHandler.StartNetworkWatching();
        });
        t.Start();
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
        Shutdown();
      }
    }

    private void FailoverHandlerOnStateChanged(bool connectedToMain)
    {
      _notifyIcon!.Dispatcher.Invoke(() => _notifyIcon!.IconSource = connectedToMain ? _green : _yellow);
    }

    protected override void OnExit(ExitEventArgs e)
    {
      _failoverHandler!.Shutdown();
      _notifyIcon!.Dispose();
      base.OnExit(e);
    }

    private void StayOnMain_OnClick(object sender, RoutedEventArgs e)
    {
      _failoverHandler!.StayOnMain();
    }

    private void StayOnBackup_OnClick(object sender, RoutedEventArgs e)
    {
      _failoverHandler!.StayOnBackup();
    }
    
    private void AutomaticMode_OnClick(object sender, RoutedEventArgs e)
    {
      _failoverHandler!.AutomaticMode();
    }
    
    private void Exit_OnClick(object sender, RoutedEventArgs e)
    {
      Shutdown();
    }

    private ICommand ShowLogCommand
    {
      get
      {
        return new DelegateCommand
        {
          CanExecuteFunc = () => true,
          CommandAction = () =>
          {
            MainWindow!.Show();
          }
        };
      }
    }
  }
}