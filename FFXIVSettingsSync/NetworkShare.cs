using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FFXIVSettingsSync
{
    /*
        EXAMPLE:
        using (var networkShare = new NetworkShare("MyComputerName", "DomainName", "UserName", "Password"))
        {
            try
            {
                networkShare.Connect();

                var directories = Directory.GetDirectories("\\\\MyComputerName\\ShareName");

                foreach (var directory in directories)
                {
                    // Do Something
                }
            }
            catch (Exception ex)
            {
                // Do Something
            }
        }
    */
    public class NetworkShare : IDisposable
    {
        #region Constants

        private const int RESOURCE_CONNECTED = 0x00000001;
        private const int RESOURCE_GLOBALNET = 0x00000002;
        private const int RESOURCE_REMEMBERED = 0x00000003;

        private const int RESOURCETYPE_ANY = 0x00000000;
        private const int RESOURCETYPE_DISK = 0x00000001;
        private const int RESOURCETYPE_PRINT = 0x00000002;

        private const int RESOURCEDISPLAYTYPE_GENERIC = 0x00000000;
        private const int RESOURCEDISPLAYTYPE_DOMAIN = 0x00000001;
        private const int RESOURCEDISPLAYTYPE_SERVER = 0x00000002;
        private const int RESOURCEDISPLAYTYPE_SHARE = 0x00000003;
        private const int RESOURCEDISPLAYTYPE_FILE = 0x00000004;
        private const int RESOURCEDISPLAYTYPE_GROUP = 0x00000005;

        private const int RESOURCEUSAGE_CONNECTABLE = 0x00000001;
        private const int RESOURCEUSAGE_CONTAINER = 0x00000002;

        private const int CONNECT_INTERACTIVE = 0x00000008;
        private const int CONNECT_PROMPT = 0x00000010;
        private const int CONNECT_REDIRECT = 0x00000080;
        private const int CONNECT_UPDATE_PROFILE = 0x00000001;
        private const int CONNECT_COMMANDLINE = 0x00000800;
        private const int CONNECT_CMD_SAVECRED = 0x00001000;

        private const int CONNECT_LOCALDRIVE = 0x00000100;

        #endregion

        #region Error Constants

        private const int NO_ERROR = 0;

        private const int ERROR_ACCESS_DENIED = 5;
        private const int ERROR_ALREADY_ASSIGNED = 85;
        private const int ERROR_BAD_DEVICE = 1200;
        private const int ERROR_BAD_NET_NAME = 67;
        private const int ERROR_BAD_PROVIDER = 1204;
        private const int ERROR_CANCELLED = 1223;
        private const int ERROR_EXTENDED_ERROR = 1208;
        private const int ERROR_INVALID_ADDRESS = 487;
        private const int ERROR_INVALID_PARAMETER = 87;
        private const int ERROR_INVALID_PASSWORD = 1216;
        private const int ERROR_MORE_DATA = 234;
        private const int ERROR_NO_MORE_ITEMS = 259;
        private const int ERROR_NO_NET_OR_BAD_PATH = 1203;
        private const int ERROR_NO_NETWORK = 1222;

        private const int ERROR_BAD_PROFILE = 1206;
        private const int ERROR_CANNOT_OPEN_PROFILE = 1205;
        private const int ERROR_DEVICE_IN_USE = 2404;
        private const int ERROR_NOT_CONNECTED = 2250;
        private const int ERROR_OPEN_FILES = 2401;

        #endregion

        #region PInvoke Signatures

        [DllImport("Mpr.dll")]
        private static extern int WNetUseConnection(
            IntPtr hwndOwner,
            NETRESOURCE lpNetResource,
            string lpPassword,
            string lpUserID,
            int dwFlags,
            string lpAccessName,
            string lpBufferSize,
            string lpResult
            );

        [DllImport("Mpr.dll")]
        private static extern int WNetCancelConnection2(
            string lpName,
            int dwFlags,
            bool fForce
            );

        [StructLayout(LayoutKind.Sequential)]
        private class NETRESOURCE
        {
            public int dwScope = 0;
            public int dwType = 0;
            public int dwDisplayType = 0;
            public int dwUsage = 0;
            public string lpLocalName = "";
            public string lpRemoteName = "";
            public string lpComment = "";
            public string lpProvider = "";
        }

        #endregion

        private string _RemoteUncName;
        private string _UserName;
        private string _Password;

        public NetworkShare(string remoteComputerName)
            : this(remoteComputerName, null, null)
        {
        }

        public NetworkShare(string remoteComputerName, string domainName, string userName, string password)
            : this(remoteComputerName, BuildUserName(domainName, userName), password)
        {
        }

        public NetworkShare(string remoteComputerName, string userName, string password)
        {
            _RemoteUncName = $"\\\\{remoteComputerName}";
            _UserName = userName;
            _Password = password;
        }

        public void Connect()
        {
            var netResource = new NETRESOURCE
            {
                dwType = RESOURCETYPE_DISK,
                lpRemoteName = _RemoteUncName
            };
            var result = String.IsNullOrEmpty(_UserName) || String.IsNullOrEmpty(_Password)
                ? WNetUseConnection(IntPtr.Zero, netResource, String.Empty, String.Empty, CONNECT_INTERACTIVE | CONNECT_PROMPT, null, null, null)
                : WNetUseConnection(IntPtr.Zero, netResource, _Password, _UserName, 0, null, null, null);

            if (result != NO_ERROR)
            {
                throw new Win32Exception(result);
            }
        }

        public void Disconnect()
        {
            var result = WNetCancelConnection2(_RemoteUncName, CONNECT_UPDATE_PROFILE, false);

            if (result != NO_ERROR)
            {
                throw new Win32Exception(result);
            }
        }

        public void Dispose()
        {
            _UserName = String.Empty;
            _Password = String.Empty;
            Disconnect();
            GC.SuppressFinalize(this);
        }

        private static String BuildUserName(string domainName, string userName)
        {
            if (String.IsNullOrEmpty(domainName)) return userName;

            return $"{domainName}\\{userName}";
        }

        ~NetworkShare()
        {
            Dispose();
        }
    }
}
