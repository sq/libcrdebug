using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using crdebug.RemoteTypes;
using crdebug.Exceptions;

namespace crdebug {
    public partial class DebugClient {
        public class APIInstance {
            class ObjectOrException<T> where T : RemoteObject {
                public T result;
                public ExceptionDetails exceptionDetails;
            }

            class EnableState {
                public int Count;
                public bool IsEnabled {
                    get {
                        return Count > 0;
                    }
                }
            }

            class GetBoxModelResult {
                public BoxModel model;
            }

            class NodeIds {
                public int[] nodeIds;
            }

            class BoxedNode {
                public Node node;
            }

            class Root {
                public Node root;
            }

            class Screenshot {
                public string data;
            }

            class ResponseBody {
                public string body;
                public bool base64Encoded;
            }

            struct GetPropertiesResult {
                public PropertyDescriptor[] result;
                public ExceptionDetails exceptionDetails;
            }

            public readonly Dictionary<string, Frame> Frames = new Dictionary<string, Frame>();
            public readonly Dictionary<string, Request> Requests = new Dictionary<string, Request>();
            public readonly Dictionary<string, Response> Responses = new Dictionary<string, Response>();

            private readonly Dictionary<string, EnableState> EnableStates = new Dictionary<string, EnableState>();

            public readonly DebugClient Client;

            private Node CachedDocument = null;

            public event Action<Frame> OnNavigated;
            public event Action<string, Frame> OnUrlChanged;
            public event Action<string> OnStartedLoading;
            public event Action<Request> OnRequestStarted;
            public event Action<Request, Response> OnRequestComplete;
            public event Action OnLoaded;

            private int NextGroupId = 1;

            public APIInstance (DebugClient client) {
                Client = client;
                Client.OnEvent += Client_OnEvent;
            }

            private void Client_OnEvent (string method, Newtonsoft.Json.Linq.JToken args) {
                Frame frame = null;

                switch (method) {
                    case "Page.navigatedWithinDocument":
                        var newUrl = args["url"].ToString();
                        Frames.TryGetValue(args["frameId"].ToString(), out frame);
                        OnUrlChanged?.Invoke(newUrl, frame);
                        break;
                    case "Page.frameNavigated":
                        frame = args["frame"].ToObject<Frame>();
                        Frames[frame.id] = frame;
                        OnNavigated?.Invoke(frame);
                        break;
                    case "Page.frameStartedLoading":
                        OnStartedLoading?.Invoke(args["frameId"].ToString());
                        break;
                    case "Page.loadEventFired":
                        OnLoaded?.Invoke();
                        break;
                    case "Network.loadingFinished":
                        var reqId = args["requestId"].ToString();
                        if (Responses.TryGetValue(reqId, out Response resp))
                            OnRequestComplete?.Invoke(resp.Request, resp);
                        break;
                    case "Network.responseReceived":
                        reqId = args["requestId"].ToString();
                        if (Requests.TryGetValue(reqId, out Request req))
                            Requests.Remove(reqId);
                        resp = args["response"].ToObject<Response>();
                        resp.Request = req;
                        resp.requestId = reqId;
                        Responses[reqId] = resp;
                        // HACK: Chrome sucks and the response is not actually ready yet.
                        // OnRequestComplete?.Invoke(req, resp);
                        break;
                    case "Network.requestWillBeSent":
                        reqId = args["requestId"].ToString();
                        req = args.ToObject<Request>();
                        req.frameId = args["frameId"].ToString();
                        Frames.TryGetValue(req.frameId, out req.Frame);
                        req.requestId = reqId;
                        Requests[reqId] = req;
                        OnRequestStarted?.Invoke(req);
                        break;
                }
            }

            private EnableState GetEnableState (string category) {
                EnableState result;
                if (!EnableStates.TryGetValue(category, out result))
                    EnableStates[category] = result = new EnableState();
                return result;
            }

            public async Task Enable (string category) {
                var s = GetEnableState(category);
                var enabled = s.IsEnabled;
                s.Count++;

                if (enabled)
                    return;
                await Client.Send($"{category}.enable");
            }

            public async Task Disable (string category) {
                var s = GetEnableState(category);
                var enabled = s.IsEnabled;
                s.Count--;

                if (enabled && !s.IsEnabled)
                    await Client.Send($"{category}.disable");
            }

            private void CheckEnabled (string category) {
                var s = GetEnableState(category);
                if (!s.IsEnabled)
                    throw new InvalidOperationException("DOM must be enabled");
            }

            public async Task<NodeId> NodeForLocation (int x, int y) {
                CheckEnabled("DOM");
                var result = await Client.SendAndGetResult<NodeId>("DOM.getNodeForLocation", new {
                    x, y
                });
                return new NodeId(result.nodeId);
            }

            public async Task<Node> GetDocument (bool cached = true, int depth = 3) {
                if (CachedDocument != null) {
                    if (CachedDocument.depth < depth)
                        cached = false;
                }
                if (cached && CachedDocument != null)
                    return CachedDocument;
                var result = await Client.SendAndGetResult<Root>("DOM.getDocument", new {
                    depth
                });
                return (CachedDocument = result.root);
            }

            public async Task<Node> GetBody (bool cached = true) {
                var doc = await GetDocument(cached);
                var html = doc.children.Last();
                var body = html.children.FirstOrDefault(node => node.localName.ToLowerInvariant() == "body");
                return body;
            }

            public async Task<Node> DescribeNode (NodeId id, int depth = 1) {
                var result = await Client.SendAndGetResult<BoxedNode>("DOM.describeNode", new {
                    id.nodeId, depth
                });
                return result.node;
            }

            public async Task<List<Node>> DescribeNodes (IEnumerable<NodeId> ids, int depth = 1) {
                var tasks = new List<Task<BoxedNode>>();
                foreach (var id in ids) {
                    tasks.Add(
                        Client.SendAndGetResult<BoxedNode>("DOM.describeNode", new { id.nodeId, depth })
                    );
                }

                var result = new List<Node>();
                foreach (var task in tasks)
                    result.Add((await task).node);
                return result;
            }

            public async Task<NodeId> QuerySelector (string selector, int? parentNodeId = null) {
                int nodeId;
                if (parentNodeId.HasValue)
                    nodeId = parentNodeId.Value;
                else
                    nodeId = (await GetDocument()).Id;

                var result = await Client.SendAndGetResult<NodeId>("DOM.querySelector", new {
                    nodeId, selector
                });
                return result;
            }

            public async Task<IEnumerable<NodeId>> QuerySelectorAll (string selector, int? parentNodeId = null) {
                int nodeId;
                if (parentNodeId.HasValue)
                    nodeId = parentNodeId.Value;
                else
                    nodeId = (await GetDocument()).Id;

                var result = await Client.SendAndGetResult<NodeIds>("DOM.querySelectorAll", new {
                    nodeId, selector
                });
                return result.nodeIds.Select(i => new NodeId(i));
            }

            public async Task<BoxModel> GetBoxModel (NodeId id) {
                try {
                    var result = await Client.SendAndGetResultOrException<GetBoxModelResult>("DOM.getBoxModel", new {
                        id.nodeId
                    });
                    if (result) {
                        result.Result.model.Id = id;
                        return result.Result.model;
                    } else {
                        return default(BoxModel);
                    }
                } catch (ChromeRemoteException) {
                    return default(BoxModel);
                }
            }

            public async Task<byte[]> CaptureScreenshot (string format = "jpeg", int quality = 75) {
                var result = await Client.SendAndGetResult<Screenshot>("Page.captureScreenshot", new {
                    format, quality
                });
                return Convert.FromBase64String(result.data);
            }

            private async Task<ResponseBody> GetResponseBodyInternal (string requestId) {
                var result = await Client.SendAndGetResultOrException<ResponseBody>("Network.getResponseBody", new {
                    requestId
                });
                if (result.Exception == null)
                    return result.Result;

                var exc = result.Exception.SourceException as ChromeRemoteException;
                if (exc.Code == -32000)
                    return null;

                result.Exception.Throw();
                return null;
            }

            public async Task<byte[]> GetResponseBody (string requestId) {
                var result = await GetResponseBodyInternal(requestId);
                if (result?.base64Encoded ?? false)
                    return Convert.FromBase64String(result.body);
                else if (result != null)
                    return Encoding.UTF8.GetBytes(result.body);
                else
                    return null;
            }

            public async Task<string> GetResponseBodyText (string requestId) {
                var result = await GetResponseBodyInternal(requestId);
                if (result?.base64Encoded ?? false)
                    return Encoding.UTF8.GetString(Convert.FromBase64String(result.body));
                else
                    return result?.body;
            }

            public async Task<LayoutMetrics> GetLayoutMetrics () {
                var result = Client.SendAndGetResult<LayoutMetrics>("Page.getLayoutMetrics");
                return await result;
            }

            public async Task Click (int x, int y) {
                var p = new {
                    type = "mousePressed",
                    x, y,
                    button = "left",
                    clickCount = 1
                };
                await Client.Send("Input.dispatchMouseEvent", p);
                var p2 = new {
                    type = "mouseReleased",
                    x, y,
                    button = "left",
                    clickCount = 1
                };
                await Client.Send("Input.dispatchMouseEvent", p2);
            }

            public Task ClickRandomPoint (BoxModel box, Random rng) {
                return Click(
                    rng.Next(box.ContentLeft, box.ContentRight), 
                    rng.Next(box.ContentTop, box.ContentBottom)
                );
            }

            public async Task<PageLoadEventArgs> WaitForPageLoad () {
                CheckEnabled("Page");
                var f = Client.AwaitEvent<PageLoadEventArgs>("Page.loadEventFired");
                return await f;
            }

            public async Task Reload () {
                await Client.Send("Page.reload");
            }

            public async Task<NavigateResult> Navigate (string url, string transitionType = "address_bar") {
                CheckEnabled("Page");
                var result = await Client.SendAndGetResult<NavigateResult>("Page.navigate", new {
                    url, transitionType
                });
                return result;
            }

            internal void TryAnnotateWithClient (RemoteObject o) {
                if (o == null)
                    return;
                o.Client = Client;
            }

            public async Task<RemoteObject> Evaluate (string expression)  {
                var objectGroup = $"evaluate{NextGroupId++}";
                var result = await Client.SendAndGetResult<ObjectOrException<RemoteObject>>("Runtime.evaluate", new {
                    expression
                    // objectGroup
                });

                TryAnnotateWithClient(result.exceptionDetails?.exception);

                if (result.exceptionDetails != null)
                    throw new ChromeRemoteJSException(result.exceptionDetails);

                result.result.Group = new RemoteObjectGroup(Client, objectGroup);
                return result.result;
            }

            public async Task<RemoteObject> ExecuteScriptResource<T> (string name, Assembly assembly) where T : RemoteObject {
                string source;
                using (var s = assembly.GetManifestResourceStream(name))
                using (var sr = new StreamReader(s, Encoding.UTF8))
                    source = sr.ReadToEnd();

                var obj = await Evaluate(source);
                return obj;
            }

            public async Task<RemoteObject> CallFunctionOn (RemoteObject @this, string functionDeclaration, params object[] arguments) {
                var callArguments = new List<object>();

                if (arguments != null)
                foreach (var a in arguments) {
                    var ro = a as RemoteObject;
                    if (ro != null) {
                        if (ro.objectId != null)
                            callArguments.Add(new { ro.objectId });
                        else
                            callArguments.Add(new { ro.value });
                    } else {
                        callArguments.Add(new { value = a });
                    }
                }

                var objectGroup = $"callFunctionOn{NextGroupId++}";

                var result = await Client.SendAndGetResult<ObjectOrException<RemoteObject>>("Runtime.callFunctionOn", new {
                    functionDeclaration,
                    @this.objectId,
                    arguments = callArguments.ToArray(),
                    userGesture = true,
                    objectGroup
                });

                TryAnnotateWithClient(result.exceptionDetails?.exception);

                if (result.exceptionDetails != null)
                    throw new ChromeRemoteJSException(result.exceptionDetails);

                result.result.Group = new RemoteObjectGroup(Client, objectGroup);
                return result.result;
            }

            public Task<RemoteObject> CallMethod (RemoteObject @this, string methodName, params object[] arguments) {
                var fn = "function () { return this." + methodName + ".apply(this, arguments); }";
                return CallFunctionOn(@this, fn, arguments);
            }

            public async Task<RemoteObject> AwaitPromise (RemoteObject promise) {
                var result = await Client.SendAndGetResult<ObjectOrException<RemoteObject>>("Runtime.awaitPromise", new {
                    promiseObjectId = promise.objectId
                });

                TryAnnotateWithClient(result.result);
                TryAnnotateWithClient(result.exceptionDetails?.exception);

                if (result.exceptionDetails != null)
                    throw new ChromeRemoteJSException(result.exceptionDetails);

                return result.result;
            }

            public async Task HighlightNode (NodeId id) {
                await Client.Send("DOM.highlightNode", new {
                    highlightConfig = new {
                        borderColor = new {
                            r = 64,
                            g = 200,
                            b = 255,
                            a = 0.8f
                        },
                        contentColor = new {
                            r = 64,
                            g = 200,
                            b = 255,
                            a = 0.33f
                        }
                    },
                    id.nodeId
                });
            }

            public async Task<PropertyDescriptor[]> GetProperties (
                RemoteObject obj, bool ownProperties = false, bool accessorPropertiesOnly = false
            ) {
                var result = await Client.SendAndGetResult<GetPropertiesResult>(
                    "Runtime.getProperties", new {
                        obj.objectId, ownProperties, accessorPropertiesOnly
                    }
                );

                TryAnnotateWithClient(result.exceptionDetails?.exception);
                if (result.exceptionDetails != null)
                    throw new ChromeRemoteJSException(result.exceptionDetails);

                if (obj.Group == null) {
                    foreach (var p in result.result) {
                        TryAnnotateWithClient(p.value);
                        TryAnnotateWithClient(p.get);
                        TryAnnotateWithClient(p.set);
                        TryAnnotateWithClient(p.symbol);
                    }
                }
                return result.result;
            }

            public async Task HideHighlight () {
                await Client.Send("DOM.hideHighlight");
            }
        }
    }
}
