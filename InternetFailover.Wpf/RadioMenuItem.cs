using System.Linq;
using System.Windows.Controls;

namespace InternetFailover.Wpf;

public class RadioMenuItem : MenuItem
{
  protected override void OnClick()
  {
    if (Parent is ItemsControl ic)
    {
      var rmi = ic.Items.OfType<RadioMenuItem>().FirstOrDefault(i => i.IsChecked);
      if (null != rmi) rmi.IsChecked = false;
    }
    base.OnClick();
  }
}
