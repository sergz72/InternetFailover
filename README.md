# InternetFailover

Allows to switch to backup WiFi network in case of Internet issues in the main WiFi network (failover).
Requires static IP without default gateway to be set in IPV4 configuration on the both network adapters.
Requires appsettings.json configuration file with the following parameters:

```json
{
  "Settings": {
    "TestIP": "???",
    "PingInterval": 2,
    "PingTimeout": 3,
    "PingFailuresBeforeSwitchToBackup": 5,
    "SuccessPingsBeforeSwitchToMain": 5,
    "MainInterface": "???",
    "MainInterfaceSSID": "???",
    "MainInterfaceName": "???",
    "MainNetworkName": "???",
    "BackupInterface": "???",
    "BackupInterfaceSSID": "???",
    "BackupInterfaceName": "???",
    "BackupNetworkName": "???"
  }
}
```

Parameters meanings:

- TestIP - IP address to which the application will send ICMP requests to check that internet connection is alive. Example value : 8.8.8.8
- PingInterval - interval in seconds between ICMP requests to TestIP. Example value: 2.
- PingTimeout - ICMP request timeout in seconds. Example value: 3.
- PingFailuresBeforeSwitchToBackup - number of ICMP request failures in a row that invokes switch from main to backup internet connection. Example value: 5.
- SuccessPingsBeforeSwitchToMain - number of ICMP request successes in a row that invokes switch from backup to main internet connection. Example value: 5.
- MainInterface - main internet interface gateway IP address ( WiFi router IP address ).
- MainInterfaceSSID - WiFi SSID of main internet interface.
- MainInterfaceName - network adapter name for main internet interface.
- MainNetworkName - network name for main internet interface.
- BackupInterface - backup internet interface gateway IP address ( WiFi router IP address ).
- BackupInterfaceSSID - WiFi SSID of backup internet interface.
- BackupInterfaceName - network adapter name for backup internet interface.
- BackupNetworkName - network name for backup internet interface.
