using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using crdebug;
using crdebug.RemoteTypes;

namespace QuerySelector {
    class Program {
        public static void Main (string[] args) {
            try {
                var t = AsyncMain(args);

                Console.WriteLine("Waiting for test completion");
                if (!t.Wait(60000)) {
                    Console.WriteLine("Test timed out");
                    if (Debugger.IsAttached)
                        Console.ReadLine();
                    Environment.Exit(2);
                }

                Console.WriteLine("Tests finished successfully");
                if (Debugger.IsAttached)
                    Console.ReadLine();
                Environment.Exit(0);
            } catch (Exception exc) {
                Console.WriteLine(exc);
                if (Debugger.IsAttached)
                    Console.ReadLine();
                Environment.Exit(1);
            }
        }

        public static async Task AsyncMain (string[] args) {
            var browser = new BrowserInstance(IPAddress.Loopback, 8881);
            var tabs = await browser.EnumerateTabs();
            var tab = tabs.FirstOrDefault(t => t.url.Contains("queryselector.html"));
            if (tab == null)
                throw new Exception("No tab containing queryselector.html found");

            var conn = new DebugClient(browser, tab);
            conn.ReceiveTraceStream = Console.Out;
            conn.SendTraceStream = Console.Out;
            Console.WriteLine("Connecting");
            await conn.Connect();

            Console.WriteLine("Querying selectors");
            var div = await conn.API.QuerySelector("div");
            if (!div)
                throw new Exception("Failed to find div");
            var divDescription = await conn.API.DescribeNode(div, 0);
            var span1 = await conn.API.QuerySelector("span#span1");
            var span2 = await conn.API.QuerySelector("span#span2", divDescription.Id);
            if (!span1 || !span2)
                throw new Exception("Failed to find span1 and span2");

            Console.WriteLine("Querying inside selector");

            await CheckDescriptions(conn, span1, span2);

            Console.WriteLine("Waiting 10 seconds");
            await Task.Delay(10000);
            await CheckDescriptions(conn, span1, span2);
        }

        static async Task CheckDescriptions (DebugClient conn, NodeId span1, NodeId span2) {
            Console.WriteLine("Getting descriptions");
            var span1Desc = await conn.API.DescribeNode(span1);
            var span2Desc = await conn.API.DescribeNode(span2);
            if ((span1Desc == null) || (span2Desc == null))
                throw new Exception("Failed to get descriptions of span1 and span2");
            if (span1Desc.nodeValue != "1")
                throw new Exception("Span1 text was not 1");
            if (span1Desc.nodeValue != "2")
                throw new Exception("Span1 text was not 1");
        }
    }
}
