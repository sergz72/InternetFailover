using System.Net;
using System.Net.NetworkInformation;
using CodeCowboy.NetworkRoute;
using ManagedNativeWifi;
using Microsoft.Extensions.Configuration;

namespace InternetFailover.Core;

public abstract class InternetFailover
{
  private class ConfigurationException : Exception
  {
    public ConfigurationException(string message, params object[] parameters): base(String.Format(message, parameters))
    {
    }
  }
  
  private static readonly IPAddress ZeroIp = IPAddress.Parse("0.0.0.0");
  private static readonly IPAddress OneIpMask = IPAddress.Parse("255.255.255.255");
  
  private readonly IPAddress _testIp;
  private readonly IPAddress _mainInterfaceIp;
  private readonly IPAddress _backupInterfaceIp;
  private readonly int _pingInterval;
  private readonly int _pingFailuresBeforeSwitchToBackup;
  private readonly int _successPingsBeforeSwitchToMain;
  private readonly string _mainInterfaceSsid;
  private readonly string _mainInterfaceName;
  private readonly string _backupInterfaceSsid;
  private readonly string _backupInterfaceName;
  private readonly int _mainInterfaceIndex;
  private readonly int _backupInterfaceIndex;
  private readonly Guid _mainInterfaceId;
  private readonly Guid _backupInterfaceId;

  protected InternetFailover()
  {
    IConfiguration config = new ConfigurationBuilder()
      .AddJsonFile("appsettings.json")
      .Build();

    var section = config.GetRequiredSection("Settings");
    _testIp = GetIpVariable(section, "TestIP");
    _pingInterval = GetIntVariable(section, "PingInterval") * 1000;
    _pingFailuresBeforeSwitchToBackup = GetIntVariable(section, "PingFailuresBeforeSwitchToBackup");
    _successPingsBeforeSwitchToMain = GetIntVariable(section, "SuccessPingsBeforeSwitchToMain");
    _mainInterfaceIp = GetIpVariable(section, "MainInterface");
    _mainInterfaceSsid = GetStringVariable(section, "MainInterfaceSSID");
    _mainInterfaceName = GetStringVariable(section, "MainInterfaceName");
    _backupInterfaceIp = GetIpVariable(section, "BackupInterface");
    _backupInterfaceSsid = GetStringVariable(section, "BackupInterfaceSSID");
    _backupInterfaceName = GetStringVariable(section, "BackupInterfaceName");

    NetworkInterface? mainInterface = null;
    NetworkInterface? backupInterface = null;
    foreach (var i in NetworkInterface.GetAllNetworkInterfaces())
    {
      foreach (var address in i.GetIPProperties().UnicastAddresses)
      {
        var a = address.Address.GetAddressBytes();
        if (a.Length == 4)
        {
          var m = address.IPv4Mask.GetAddressBytes();
          var ma = _mainInterfaceIp.GetAddressBytes();
          var ba = _backupInterfaceIp.GetAddressBytes();
          var isMainInterface = true;
          var isBackupInterface = true;
          for (int n = 0; n < a.Length; n++)
          {
            var b = (byte)(a[n] & m[n]);
            if ((byte)(ma[n] & m[n]) != b)
              isMainInterface = false;
            if ((byte)(ba[n] & m[n]) != b)
              isBackupInterface = false;
          }

          if (isMainInterface)
          {
            if (isBackupInterface)
              throw new ConfigurationException("Main and Backup interfaces are in the same address range");

            mainInterface = i;
          }
          else if (isBackupInterface)
            backupInterface = i;
        }
      }
    }
    CheckInterface(mainInterface, "main");
    CheckInterface(backupInterface, "backup");
    _mainInterfaceIndex = mainInterface!.GetIPProperties().GetIPv4Properties().Index;
    _backupInterfaceIndex = backupInterface!.GetIPProperties().GetIPv4Properties().Index;
    _mainInterfaceId = GetInterfaceId(_mainInterfaceName, "Main");
    _backupInterfaceId = GetInterfaceId(_backupInterfaceName, "Backup");
  }

  public void LogConfiguration()
  {
    Log("TestIP = {0}", _testIp);
    Log("PingInterval = {0}", _pingInterval);
    Log("PingFailuresBeforeSwitchToBackup = {0}", _pingFailuresBeforeSwitchToBackup);
    Log("SuccessPingsBeforeSwitchToMain = {0}", _successPingsBeforeSwitchToMain);
    Log("MainInterface = {0}", _mainInterfaceIp);
    Log("MainInterfaceSSID = {0}", _mainInterfaceSsid);
    Log("MainInterfaceName = {0}", _mainInterfaceName);
    Log("BackupInterface = {0}", _backupInterfaceIp);
    Log("BackupInterfaceSSID = {0}", _backupInterfaceSsid);
    Log("BackupInterfaceName = {0}", _backupInterfaceName);
  }
  
  public void Prepare()
  {
    StartWiFiWatching();
    WaitForWiFi();
    PrepareRouteTable();
  }
  
  protected abstract void Log(string message, params object[] parameters);
  
  private string GetStringVariable(IConfigurationSection section, string name)
  {
    var v = section[name];
    if (string.IsNullOrEmpty(v))
      throw new ConfigurationException("{0} configuration parameter is missing", name);
    return v;
  }

  private IPAddress GetIpVariable(IConfigurationSection section, string name)
  {
    var v = section[name];
    if (string.IsNullOrEmpty(v))
      throw new ConfigurationException("{0} configuration parameter is missing", name);
    return IPAddress.Parse(v);
  }

  private int GetIntVariable(IConfigurationSection section, string name)
  {
    var str = section[name];
    if (string.IsNullOrEmpty(str))
      throw new ConfigurationException("{0} configuration parameter is missing", name);
    if (!int.TryParse(str, out var v))
      throw new ConfigurationException("{0} configuration parameter should have numeric value", name);
    if (v <= 0)
      throw new ConfigurationException("{0} configuration parameter should be > 0", name);
    return v;
  }

  private void CheckInterface(NetworkInterface? i, string name)
  {
    if (i == null)
      throw new ConfigurationException("{0} interface not found", name);
    if (i.GetIPProperties().GetIPv4Properties().IsDhcpEnabled)
      throw new ConfigurationException("Please disable DHCP for {0} interface.", name);
  }

  private void CreateRoute(IPAddress destination, IPAddress mask, IPAddress gw, int interfaceIndex)
  {
    Ip4RouteEntry e = new Ip4RouteEntry
    {
      DestinationIP = destination,
      GatewayIP = gw,
      SubnetMask = mask,
      InterfaceIndex = interfaceIndex
    };
    Log("Creating route entry {0} mask {1} gw {2} if {3}...", e.DestinationIP, e.SubnetMask, e.GatewayIP, e.InterfaceIndex);
    Ip4RouteTable.CreateRoute(e);
  }

  private void DeleteRoute(Ip4RouteEntry entry)
  {
    Log("Deleting route entry {0} mask {1} gw {2} if {3}...", entry.DestinationIP, entry.SubnetMask, entry.GatewayIP, entry.InterfaceIndex);
    Ip4RouteTable.DeleteRoute(entry);
  }

  private void CleanRouteTable()
  {
    Log("Route table check....");
    foreach (var entry in Ip4RouteTable.GetRouteTable())
    {
      if (entry.DestinationIP.Equals(_testIp) || entry.DestinationIP.Equals(ZeroIp))
        DeleteRoute(entry);
    }

    CreateRoute(_testIp, OneIpMask, _mainInterfaceIp, _mainInterfaceIndex);
  }

  private void PrepareRouteTable()
  {
    CleanRouteTable();
    CreateRoute(ZeroIp, ZeroIp, _mainInterfaceIp, _mainInterfaceIndex);
  }


  private void SwitchTo(IPAddress a, string name, int interfaceIndex)
  {
    Log("{0} Switching to {1}...", DateTime.Now, name);
    CleanRouteTable();
    CreateRoute(ZeroIp, ZeroIp, a, interfaceIndex);
  }

  public void StartNetworkWatching()
  {
    var p = new Ping();
    var options = new PingOptions
    {
      DontFragment = true
    };
    var data = new byte[32];
    var rnd = new Random();
    rnd.NextBytes(data);
    var failureCounter = 0;
    var successCounter = -1;
    for (;;)
    {
      var status = IPStatus.Unknown;
      var pingException = false;
      try
      {
        var result = p.Send(_testIp, 1500, data, options);
        status = result.Status;
      }
      catch (Exception e)
      {
        Log("{0} Ping exception: {1}", DateTime.Now, e.Message);
        pingException = true;
      }

      if (status != IPStatus.Success)
      {
        if (!pingException)
          Log("{0} Ping result {1}", DateTime.Now, status);
        if (successCounter > 0)
          successCounter = 0;
        if (failureCounter >= 0)
        {
          failureCounter++;
          if (failureCounter >= _pingFailuresBeforeSwitchToBackup)
          {
            successCounter = 0;
            failureCounter = -1;
            SwitchTo(_backupInterfaceIp, "backup", _backupInterfaceIndex);
          }
        }
      }
      else
      {
        if (failureCounter > 0)
          failureCounter = 0;
        if (successCounter >= 0)
        {
          successCounter++;
          if (successCounter >= _successPingsBeforeSwitchToMain)
          {
            successCounter = -1;
            failureCounter = 0;
            SwitchTo(_mainInterfaceIp, "main", _mainInterfaceIndex);
          }
        }
      }
      Thread.Sleep(_pingInterval);
    }
  }

  private void ConnectWiFi(string interfaceName, Guid interfaceId, string ssid)
  {
    var availableNetwork = NativeWifi
      .EnumerateAvailableNetworks()
      .FirstOrDefault(x => x.Ssid.ToString() == ssid);

    if (availableNetwork != null)
    {
      try
      {
        Log("Connecting {0} to {1}...", interfaceName, ssid);
        NativeWifi.ConnectNetwork(interfaceId, availableNetwork.ProfileName, availableNetwork.BssType);
        Log("Connected.");
      }
      catch (Exception e)
      {
        Log("Connection failure: {0}", e.Message);
      }
    }
  }

  private void WaitForWiFi()
  {
    var failure = true;
    while (failure)
    {
      failure = false;
      foreach (var i in NativeWifi.EnumerateInterfaces())
      {
        if (i.Description == _mainInterfaceName && i.State != InterfaceState.Connected)
        {
          failure = true;
          break;
        }
        if (i.Description == _backupInterfaceName && i.State != InterfaceState.Connected)
        {
          failure = true;
          break;
        }
      }
      Thread.Sleep(_pingInterval);
    }
  }

  private Guid GetInterfaceId(string interfaceName, string name)
  {
    foreach (var i in NativeWifi.EnumerateInterfaces())
    {
      if (i.Description == interfaceName)
        return i.Id;
    }
    throw new ConfigurationException("{0} WiFi interface not found.", name);
  }
  
  private void StartWiFiWatching()
  {
    var t = new Thread(() =>
    {
      for (;;)
      {
        foreach (var i in NativeWifi.EnumerateInterfaces())
        {
          if (i.Id == _mainInterfaceId && i.State == InterfaceState.Disconnected)
          {
            ConnectWiFi(_mainInterfaceName, _mainInterfaceId, _mainInterfaceSsid);
          }
          if (i.Id == _backupInterfaceId && i.State == InterfaceState.Disconnected)
          {
            ConnectWiFi(_backupInterfaceName, _backupInterfaceId, _backupInterfaceSsid);
          }
        }
        Thread.Sleep(_pingInterval);
      }
    });
    t.Start();
  }
}
