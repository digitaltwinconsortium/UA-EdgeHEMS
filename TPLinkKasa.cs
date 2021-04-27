
using System.Collections.Generic;

namespace PVMonitor.TPLinkKasa
{
    public class LoginResponse
    {
        public int error_code { get; set; }

        public AccountInfo result { get; set; }
    }

    public class GetDeviceListResponse
    {
        public int error_code { get; set; }

        public DeviceList result { get; set; }
    }

    public class PassThroughResponse
    {
        public int error_code { get; set; }

        public ResultData result { get; set; }
    }

    public class AccountInfo
    {
        public string accountId { get; set; }

        public string regTime { get; set; }

        public string countryCode { get; set; }

        public string nickname { get; set; }

        public string email { get; set; }

        public string token { get; set; }
    }

    public class DeviceListItem
    {
        public string deviceType { get; set; }

        public int role { get; set; }

        public string fwVer { get; set; }

        public string appServerUrl { get; set; }

        public string deviceRegion { get; set; }

        public string deviceId { get; set; }

        public string deviceName { get; set; }

        public string deviceHwVer { get; set; }

        public string alias { get; set; }

        public string deviceMac { get; set; }

        public string oemId { get; set; }

        public string deviceModel { get; set; }

        public string hwId { get; set; }

        public string fwId { get; set; }

        public string isSameRegion { get; set; }

        public int status { get; set; }
    }

    public class DeviceList
    {
        public List<DeviceListItem> deviceList { get; set; }
    }

    public class ResultData
    {
        public string responseData { get; set; }
    }
}
