using System.Net;
using Microsoft.Extensions.Configuration;
using System.Net.NetworkInformation;
using CodeCowboy.NetworkRoute;
using ManagedNativeWifi;

var zeroIp = IPAddress.Parse("0.0.0.0");
var oneIpMask = IPAddress.Parse("255.255.255.255");

string GetStringVariable(IConfigurationSection section, string name)
{
  var v = section[name];
  if (string.IsNullOrEmpty(v))
  {
    Console.WriteLine("{0} configuration parameter is missing", name);
    Environment.Exit(1);
  }
  return v;
}

IPAddress GetIpVariable(IConfigurationSection section, string name)
{
  var v = section[name];
  if (string.IsNullOrEmpty(v))
  {
    Console.WriteLine("{0} configuration parameter is missing", name);
    Environment.Exit(1);
  }
  var ip = IPAddress.Parse(v);
  Console.WriteLine("{0} = {1}", name, ip);
  return ip;
}

int GetIntVariable(IConfigurationSection section, string name)
{
  var str = section[name];
  if (string.IsNullOrEmpty(str))
  {
    Console.WriteLine("{0} configuration parameter is missing", name);
    Environment.Exit(1);
  }
  if (!int.TryParse(str, out var v))
  {
    Console.WriteLine("{0} configuration parameter should have numeric value", name);
    Environment.Exit(1);
  }
  Console.WriteLine("{0} = {1}", name, v);
  return v;
}

void CheckInterface(NetworkInterface? i, string name)
{
  if (i == null)
  {
    Console.WriteLine("{0} interface not found", name);
    Environment.Exit(1);
  }
  if (i.GetIPProperties().GetIPv4Properties().IsDhcpEnabled)
  {
    Console.WriteLine("Please disable DHCP for {0} interface.", name);
    Environment.Exit(1);
  }
}

void CreateRoute(IPAddress destination, IPAddress mask, IPAddress gw, int interfaceIndex)
{
  Ip4RouteEntry e = new Ip4RouteEntry
  {
    DestinationIP = destination,
    GatewayIP = gw,
    SubnetMask = mask,
    InterfaceIndex = interfaceIndex
  };
  Console.WriteLine("Creating route entry {0} mask {1} gw {2} if {3}...", e.DestinationIP, e.SubnetMask, e.GatewayIP, e.InterfaceIndex);
  Ip4RouteTable.CreateRoute(e);
}

void DeleteRoute(Ip4RouteEntry entry)
{
  Console.WriteLine("Deleting route entry {0} mask {1} gw {2} if {3}...", entry.DestinationIP, entry.SubnetMask, entry.GatewayIP, entry.InterfaceIndex);
  Ip4RouteTable.DeleteRoute(entry);
}

void CleanRouteTable(IPAddress testIp, IPAddress mainInterfaceIp, int mainInterfaceNumber)
{
  Console.WriteLine("Route table check....");
  foreach (var entry in Ip4RouteTable.GetRouteTable())
  {
    if (entry.DestinationIP.Equals(testIp) || entry.DestinationIP.Equals(zeroIp))
      DeleteRoute(entry);
  }

  CreateRoute(testIp, oneIpMask, mainInterfaceIp, mainInterfaceNumber);
}

void PrepareRouteTable(IPAddress testIp, IPAddress mainInterfaceIp, int mainInterfaceNumber)
{
  CleanRouteTable(testIp, mainInterfaceIp, mainInterfaceNumber);
  CreateRoute(zeroIp, zeroIp, mainInterfaceIp, mainInterfaceNumber);
}


void SwitchTo(IPAddress a, string name, int interfaceIndex, IPAddress testIp, IPAddress mainInterfaceIp, int mainInterfaceNumber)
{
  Console.WriteLine("{0} Switching to {1}...", DateTime.Now, name);

  CleanRouteTable(testIp, mainInterfaceIp, mainInterfaceNumber);
  CreateRoute(zeroIp, zeroIp, a, interfaceIndex);
}

void StartNetworkWatching(IPAddress testIp, IPAddress mainIp, int mainInterfaceIndex, IPAddress backupIp, int backupInterfaceIndex,
                          int pingInterval, int pingFailuresBeforeSwitchToBackup, int successPingsBeforeSwitchToMain)
{
  pingInterval *= 1000;
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
      var result = p.Send(testIp, 1500, data, options);
      status = result.Status;
    }
    catch (Exception e)
    {
      Console.WriteLine("{0} Ping exception: {1}", DateTime.Now, e.Message);
      pingException = true;
    }

    if (status != IPStatus.Success)
    {
      if (!pingException)
        Console.WriteLine("{0} Ping result {1}", DateTime.Now, status);
      if (failureCounter >= 0)
      {
        failureCounter++;
        if (failureCounter >= pingFailuresBeforeSwitchToBackup)
        {
          successCounter = 0;
          failureCounter = -1;
          SwitchTo(backupIp, "backup", backupInterfaceIndex, testIp, mainIp, mainInterfaceIndex);
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
        if (successCounter >= successPingsBeforeSwitchToMain)
        {
          successCounter = -1;
          failureCounter = 0;
          SwitchTo(mainIp, "main", mainInterfaceIndex, testIp, mainIp, mainInterfaceIndex);
        }
      }
    }
    Thread.Sleep(pingInterval);
  }
}

void ConnectWiFi(string interfaceName, Guid interfaceId, string ssid)
{
  var availableNetwork = NativeWifi
    .EnumerateAvailableNetworks()
    .FirstOrDefault(x => x.Ssid.ToString() == ssid);

  if (availableNetwork != null)
  {
    try
    {
      Console.WriteLine("Connecting {0} to {1}...", interfaceName, ssid);
      NativeWifi.ConnectNetwork(interfaceId, availableNetwork.ProfileName, availableNetwork.BssType);
      Console.WriteLine("Connected.");
    }
    catch (Exception e)
    {
      Console.WriteLine("Connection failure: {0}", e.Message);
    }
  }
}

void WaitForWiFi(int pingInterval, string mainInterfaceName, string backupInterfaceName)
{
  pingInterval *= 1000;
  var failure = true;
  while (failure)
  {
    failure = false;
    foreach (var i in NativeWifi.EnumerateInterfaces())
    {
      if (i.Description == mainInterfaceName && i.State != InterfaceState.Connected)
      {
        failure = true;
        break;
      }
      if (i.Description == backupInterfaceName && i.State != InterfaceState.Connected)
      {
        failure = true;
        break;
      }
    }
    Thread.Sleep(pingInterval);
  }
}

void StartWiFiWatching(int pingInterval, string mainInterfaceSsid, string mainInterfaceName, string backupInterfaceSsid, string backupInterfaceName)
{
  pingInterval *= 1000;
  Guid? mainInterfaceId = null;
  Guid? backupInterfaceId = null;
  foreach (var i in NativeWifi.EnumerateInterfaces())
  {
    if (i.Description == mainInterfaceName)
      mainInterfaceId = i.Id;
    else if (i.Description == backupInterfaceName)
      backupInterfaceId = i.Id;
  }

  if (mainInterfaceId == null)
  {
    Console.WriteLine("Main WiFi interface not found.");
    Environment.Exit(1);
  }
  if (backupInterfaceId == null)
  {
    Console.WriteLine("Backup WiFi interface not found.");
    Environment.Exit(1);
  }

  var t = new Thread(() =>
  {
    for (;;)
    {
      foreach (var i in NativeWifi.EnumerateInterfaces())
      {
        if (i.Id == mainInterfaceId && i.State == InterfaceState.Disconnected)
        {
          ConnectWiFi(mainInterfaceName, mainInterfaceId.Value, mainInterfaceSsid);
        }
        if (i.Id == backupInterfaceId && i.State == InterfaceState.Disconnected)
        {
          ConnectWiFi(backupInterfaceName, backupInterfaceId.Value, backupInterfaceSsid);
        }
      }
      Thread.Sleep(pingInterval);
    }
  });
  t.Start();
}

IConfiguration config = new ConfigurationBuilder()
  .AddJsonFile("appsettings.json")
  .Build();

var section = config.GetRequiredSection("Settings");
var testIp = GetIpVariable(section, "TestIP");
var pingInterval= GetIntVariable(section, "PingInterval");
var pingFailuresBeforeSwitchToBackup = GetIntVariable(section, "PingFailuresBeforeSwitchToBackup");
var successPingsBeforeSwitchToMain = GetIntVariable(section, "SuccessPingsBeforeSwitchToMain");
var mainInterfaceIp = GetIpVariable(section, "MainInterface");
var mainInterfaceSsid = GetStringVariable(section, "MainInterfaceSSID");
var mainInterfaceName = GetStringVariable(section, "MainInterfaceName");
var backupInterfaceIp = GetIpVariable(section, "BackupInterface");
var backupInterfaceSsid = GetStringVariable(section, "BackupInterfaceSSID");
var backupInterfaceName = GetStringVariable(section, "BackupInterfaceName");

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
      var ma = mainInterfaceIp.GetAddressBytes();
      var ba = backupInterfaceIp.GetAddressBytes();
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
        {
          Console.WriteLine("Main and Backup interfaces are in the same address range");
          return;
        }

        mainInterface = i;
      }
      else if (isBackupInterface)
        backupInterface = i;
    }
  }
}
CheckInterface(mainInterface, "main");
CheckInterface(backupInterface, "backup");
StartWiFiWatching(pingInterval, mainInterfaceSsid, mainInterfaceName, backupInterfaceSsid, backupInterfaceName);
WaitForWiFi(pingInterval, mainInterfaceName, backupInterfaceName);
PrepareRouteTable(testIp, mainInterfaceIp, mainInterface!.GetIPProperties().GetIPv4Properties().Index);
StartNetworkWatching(testIp, mainInterfaceIp, mainInterface.GetIPProperties().GetIPv4Properties().Index,
  backupInterfaceIp, backupInterface!.GetIPProperties().GetIPv4Properties().Index,
  pingInterval, pingFailuresBeforeSwitchToBackup, successPingsBeforeSwitchToMain);
