namespace InternetFailover;

class InternatFalioverConsole: Core.InternetFailover
{
  public static void Main(string[] args)
  {
    try
    {
      var instance = new InternatFalioverConsole();
      instance.Prepare();
      instance.StartNetworkWatching();
    }
    catch (Exception e)
    {
      Console.WriteLine(e);
    }
  }

  protected override void Log(string message, params object[] parameters)
  {
    Console.WriteLine(message, parameters);
  }
}