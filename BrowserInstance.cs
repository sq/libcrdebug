using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using crdebug.Exceptions;
using crdebug.RemoteTypes;
using Newtonsoft.Json;

namespace crdebug {
    public class BrowserInstance {
        public readonly IPAddress Address;
        public readonly int Port;

        /// <summary>
        /// Represents a running instance of Chrome with the remote debugging protocol enabled.
        /// </summary>
        /// <param name="address"></param>
        /// <param name="port"></param>
        public BrowserInstance (IPAddress address, int port) {
            Address = address;
            Port = port;
        }

        /// <summary>
        /// Enumerates the tabs currently available for debugging.
        /// </summary>
        /// <returns>A list of TabInfo objects representing open tabs. These can be used to connect to a tab.</returns>
        public async Task<TabInfo[]> EnumerateTabs () {
            var wc = new WebClient();
            string tabInfoJson = null;
            try {
                tabInfoJson = await wc.DownloadStringTaskAsync($"http://{Address}:{Port}/json/list");
            } catch (Exception exc) {
                throw new ChromeConnectException(
                    "Failed to enumerate tabs. Ensure that chrome remote debugging is enabled and the specified address and port are correct.", exc
                );
            }
            if (tabInfoJson != null) {
                try {
                    return JsonConvert.DeserializeObject<TabInfo[]>(tabInfoJson);
                } catch (Exception exc) {
                    throw new ChromeConnectException("Failed to decode the JSON from the tabs list.", exc);
                }
            } else {
                throw new ChromeConnectException("No tab list was returned by the server.");
            }
        }
    }
}
