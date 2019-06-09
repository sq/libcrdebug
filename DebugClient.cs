using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using crdebug.Exceptions;
using crdebug.RemoteTypes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace crdebug {
    public delegate void ChromeRemoteEventHandler (string method, JToken args);

    public struct ResultOrException<T> {
        public T Result;
        public ExceptionDispatchInfo Exception;

        public static implicit operator bool (ResultOrException<T> roe) {
            return (roe.Exception == null);
        }
    }

    public partial class DebugClient : IDisposable {
        struct QueuedSend {
            public string Method;
            public object Parameters;
        }

        struct ErrorInfo {
            public int code;
            public string message;
        }

        /// <summary>
        /// Specifies how deep retrieved document and element descriptions should be by default
        /// </summary>
        public int DefaultDescriptionDepth = 1;

        private readonly Dictionary<int, IFuture> PendingTokens = new Dictionary<int, IFuture>();
        private readonly Dictionary<string, List<IFuture>> EventWaits = new Dictionary<string, List<IFuture>>();

        private int NextId = 1;

        public readonly BrowserInstance Browser;
        public readonly TabInfo Tab;
        public AsyncClientWebSocket WebSocket;

        public readonly APIInstance API;

        public event ChromeRemoteEventHandler OnEvent;

        private Task MainLoopInstance = null;
        private Task ActiveSendQueueTask = null;
        private readonly Queue<QueuedSend> QueuedSends = new Queue<QueuedSend>();

        /// <summary>
        /// Fired on unhandled errors. Return true to indicate that you handled the error.
        /// </summary>
        public event Func<Exception, bool> OnError;
        /// <summary>
        /// Fired whenever a new message is received. Return true to indicate that you handled it.
        /// </summary>
        public event Func<string, bool> OnIncomingJson;

        /// <summary>
        /// If specified, all outgoing json will be logged to this stream
        /// </summary>
        public TextWriter SendTraceStream;

        /// <summary>
        /// If specified, all outgoing json will be logged to this stream
        /// </summary>
        public TextWriter ReceiveTraceStream;

        public DebugClient (BrowserInstance browser, TabInfo tab) {
            Browser = browser;
            Tab = tab;
            WebSocket = new AsyncClientWebSocket();
            // KeepAlives cause chrome to disconnect (-:
            WebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(9999);
            API = new APIInstance(this);
        }

        public async Task Connect () {
            await WebSocket.ConnectAsync(new Uri(Tab.websocketDebuggerUrl), default(CancellationToken));

            MainLoopInstance = MainLoop();
        }

        private async Task SendQueueTask () {
            while (WebSocket.State == WebSocketState.Open) {
                while (QueuedSends.Count > 0) {
                    var qs = QueuedSends.Dequeue();
                    await Send(qs.Method, qs.Parameters);
                }

                return;
            }
        }

        private async Task MainLoop () {
            var readBuffer = new ArraySegment<byte>(new byte[1024 * 1024 * 16]);

            WebSocketReceiveResult wsrr;

            while (WebSocket.State == WebSocketState.Open) {
                int readOffset = 0, count = 0;
                try {
                    do {
                        var freeSpace = readBuffer.Count - readOffset;
                        if (freeSpace <= 0)
                            throw new Exception("Read buffer full");
                        wsrr = await WebSocket.ReceiveAsync(new ArraySegment<byte>(readBuffer.Array, readOffset, freeSpace), CancellationToken.None);
                        readOffset += wsrr.Count;
                        count += wsrr.Count;
                    } while (!wsrr.EndOfMessage);
                } catch (Exception exc) {
                    if ((OnError == null) || !OnError(exc))
                        Console.Error.WriteLine(exc);
                }

                string json = "";
                JObject obj = null;
                try {
                    json = Encoding.UTF8.GetString(readBuffer.Array, 0, count);
                    obj = (JObject)JsonConvert.DeserializeObject(json);
                } catch (Exception exc) {
                    if ((OnError == null) || !OnError(exc)) {
                        Console.Error.WriteLine("JSON parse error: {0}", exc);
                        Console.Error.WriteLine("// Failed json blob below //");
                        Console.Error.WriteLine(json);
                        Console.Error.WriteLine("// Failed json blob above //");
                    }
                }

                ReceiveTraceStream?.WriteLine($"recv '{json}'");

                if ((OnIncomingJson != null) && OnIncomingJson(json))
                    continue;

                if (obj == null)
                    continue;

                if (obj["id"] != null) {
                    var id = (int)obj["id"];
                    if (PendingTokens.TryGetValue(id, out IFuture token)) {
                        if (obj.TryGetValue("error", out JToken error)) {
                            var errorInfo = error.ToObject<ErrorInfo>();
                            token.SetException(ExceptionDispatchInfo.Capture(
                                new ChromeRemoteException(errorInfo.code, errorInfo.message)
                            ));
                        } else if (!obj.TryGetValue("result", out JToken result)) {
                            token.SetResult(null);
                        } else {
                            try {
                                if (token.ResultType != typeof(object))
                                    token.SetResult(result.ToObject(token.ResultType));
                                else
                                    token.SetResult(result);
                            } catch (Exception exc) {
                                token.SetException(ExceptionDispatchInfo.Capture(exc));
                            }
                        }
                        PendingTokens.Remove(id);
                    }
                } else if (obj["method"] != null) {
                    var method = obj["method"].ToString();
                    if (EventWaits.TryGetValue(method, out List<IFuture> list)) {
                        while (list.Count > 0) {
                            var f = list[0];
                            list.RemoveAt(0);
                            f.SetResult(ConvertResult(f.ResultType, obj["params"]));
                        }
                    }
                    OnEvent?.Invoke(method, obj["params"]);
                }
            }

            await WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Main loop terminated", CancellationToken.None);
        }

        public async Task Send (string method, object p = null, int? id = null, IFuture f = null) {
            var dict = new Dictionary<string, object> {
                {"method", method }
            };
            if (p != null)
                dict.Add("params", p);
            int _id = id ?? NextId++;
            dict.Add("id", _id);
            var json = JsonConvert.SerializeObject(dict, Formatting.None);

            SendTraceStream?.WriteLine($"send '{json}'");

            var bytes = Encoding.UTF8.GetBytes(json);

            var _f = f;
            if (f == null)
                _f = new Future<object>();

            PendingTokens.Add(_id, _f);

            await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, default(CancellationToken));

            // If no future was passed in to record the result, we wait on a dummy future to propagate errors.
            if (f == null)
                await _f;
        }

        private T ConvertResult<T> (object o) {
            if (o is T)
                return (T)o;

            if (o is JToken token)
                return token.ToObject<T>();

            if (o == null)
                return default(T);

            throw new InvalidCastException($"Can't convert object of type {o.GetType().Name} to {typeof(T).Name}");
        }

        private object ConvertResult (Type t, object o) {
            if (o.GetType() == t)
                return o;

            if (o is JToken token)
                return token.ToObject(t);

            if (o == null)
                return null;

            throw new InvalidCastException($"Can't convert object of type {o.GetType().Name} to {t.Name}");
        }

        public Future<T> AwaitEvent<T> (string method) {
            if (!EventWaits.TryGetValue(method, out List<IFuture> list))
                EventWaits[method] = list = new List<IFuture>();

            var result = new Future<T>();
            list.Add(result);

            return result;
        }

        public async Task<T> SendAndGetResult<T> (string method, object p = null) {
            var id = NextId++;
            var f = new Future<T>();
            await Send(method, p, id, f);
            return await f;
        }

        public async Task<ResultOrException<T>> SendAndGetResultOrException<T> (string method, object p = null) {
            var id = NextId++;
            var f = new Future<T>();
            await Send(method, p, id, f);
            await f.DoNotThrow();
            if (f.Exception != null)
                return new ResultOrException<T> {
                    Exception = ExceptionDispatchInfo.Capture(f.Exception)
                };
            else
                return new ResultOrException<T> {
                    Result = f.Result
                };
        }

        public void QueueSend (string method, object p = null) {
            lock (QueuedSends) {
                QueuedSends.Enqueue(new QueuedSend {
                    Method = method,
                    Parameters = p
                });
                if ((ActiveSendQueueTask == null) || ActiveSendQueueTask.IsCompleted)
                    ActiveSendQueueTask = SendQueueTask();
            }
        }

        public bool IsConnected {
            get {
                return (WebSocket != null) && 
                    (WebSocket.State == WebSocketState.Open) &&
                    (MainLoopInstance != null) &&
                    !MainLoopInstance.IsCompleted;
            }
        }

        public void Dispose () {
            WebSocket.Dispose();
        }
    }
}
