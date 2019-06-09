using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace crdebug.RemoteTypes {
    public struct PageLoadEventArgs {
        public object timestamp;
    }

    public struct NavigateResult {
        public string frameId;
        public string errorText;
    }

    public struct NodeId {
        public int? nodeId;
        public int? backendNodeId;

        public NodeId (int? nodeId, int? backendNodeId) {
            this.nodeId = nodeId;
            this.backendNodeId = backendNodeId;
        }

        public static implicit operator bool (NodeId id) {
            return ((id.nodeId ?? 0) != 0) || ((id.backendNodeId ?? 0) != 0);
        }
    }

    public class Node {
        internal int depth;
        public int nodeId;
        public int? backendNodeId;
        public int? parentId;

        public NodeId Id {
            get {
                return new NodeId(nodeId, backendNodeId);
            }
        }

        public NodeId? ParentId {
            get {
                if (parentId.HasValue)
                    return new NodeId(parentId.Value, null);

                return null;
            }
        }

        public string nodeName, localName, nodeValue;
        public string publicId, systemId, name, value;

        public int childNodeCount;
        public Node[] children;

        public string[] attributes;

        public IEnumerable<KeyValuePair<string, string>> Attributes {
            get {
                if (attributes == null)
                    yield break;

                for (int i = 0, n = attributes.Length; i < n; i += 2)
                    yield return new KeyValuePair<string, string>(attributes[i], attributes[i + 1]);
            }
        }

        public override string ToString () {
            return $"{nodeName} #{nodeId}";
        }

        public string GetAttribute (string name) {
            return Attributes.FirstOrDefault(a => string.Equals(a.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
        }
    }

    public struct BoxModel {
        public NodeId Id;

        public int[] content, padding, border, margin;
        public int width, height;
        internal ExceptionDispatchInfo Exception;

        public int ContentLeft {
            get {
                if (!Id)
                    return 0;
                if (Exception != null)
                    Exception.Throw();
                return content[0];
            }
        }

        public int ContentTop {
            get {
                if (!Id)
                    return 0;
                if (Exception != null)
                    Exception.Throw();
                return content[1];
            }
        }

        public int ContentRight {
            get {
                if (!Id)
                    return 0;
                if (Exception != null)
                    Exception.Throw();
                return content[2];
            }
        }

        public int ContentBottom {
            get {
                if (!Id)
                    return 0;
                if (Exception != null)
                    Exception.Throw();
                return content[5];
            }
        }

        public static implicit operator bool (BoxModel model) {
            return (model.Id.nodeId != 0) && (model.content != null) && (model.content.Length >= 6) && (model.Exception == null);
        }
    }

    public class PropertyDescriptor : IDisposable {
        public string name;
        public bool configurable, enumerable, wasThrown, isOwn, writable;

        public RemoteObject value, get, set, symbol;

        public void Dispose () {
            value?.Dispose();
            get?.Dispose();
            set?.Dispose();
            symbol?.Dispose();
        }
    }

    public class RemoteObjectGroup : IDisposable {
        public bool IsDisposed { get; internal set; }
        public DebugClient Client { get; internal set; }

        public string objectGroup;

        public RemoteObjectGroup (DebugClient client, string objectGroup) {
            Client = client;
            this.objectGroup = objectGroup;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            if (Client == null)
                return;

            IsDisposed = true;
            Client.QueueSend("Runtime.releaseObjectGroup", new { objectGroup });
        }
    }

    public class RemoteObject : IDisposable {
        public bool IsDisposed { get; internal set; }
        public DebugClient Client { get; internal set; }

        public RemoteObjectGroup Group;

        public string type, subtype, className, description, unserializableValue, objectId;
        public object value;

        public void Dispose () {
            // Not necessary to dispose primitive types, probably
            if (type != "object")
                return;

            if (Group != null) {
                Group.Dispose();
                return;
            }

            if (IsDisposed)
                return;

            if (objectId == null) {
                IsDisposed = true;
                return;
            }

            if (Client == null)
                return;

            IsDisposed = true;
            Client.QueueSend("Runtime.releaseObject", new { objectId });
        }

        public override string ToString () {
            switch (type) {
                case "string":
                    return $"string '{value.ToString()}'";
                default:
                    return $"remote {className}";
            }
        }
    }

    public class ExceptionDetails {
        public int exceptionId, lineNumber, columnNumber;
        public string text, url;
        public RemoteObject exception;
    }

    public class Frame {
        public string id, parentId, loaderId, name, url, securityOrigin, mimeType, unreachableUrl;
    }

    public class Request {
        public string requestId, frameId;
        public string url, urlFragment, method, postData, referrerPolicy;
        public bool hasPostData, isLinkPreload;
        public RemoteObject headers;
        public Frame Frame;
    }

    public class Response {
        public string requestId;
        public string url, statusText, headersText, mimeType, requestHeadersText, remoteIPAddress, protocol;
        public bool connectionReused, fromDiskCache, fromServiceWorker, fromPrefetchCache;
        public int status, remotePort;
        public RemoteObject headers, requestHeaders;
        public Request Request;
    }

    public class TabInfo {
        public string description;
        public string devtoolsFrontendUrl;
        public string id;
        public string title;
        public string type;
        public string url;
        public string websocketDebuggerUrl;
    }

    public struct DOMRect {
        public double x, y, width, height;
    }

    public struct LayoutMetrics {
        public struct LayoutViewport {
            public int pageX, pageY;
            public int clientWidth, clientHeight;
        }
        public struct VisualViewport {
            public double offsetX, offsetY;
            public double pageX, pageY;
            public double clientWidth, clientHeight;
            public double scale, zoom;
        }

        public LayoutViewport layoutViewport;
        public VisualViewport visualViewport;
        public DOMRect contentSize;
    }

    public class ScreencastFrameMetadata {
        public double offsetTop, pageScaleFactor;
        public double deviceWidth, deviceHeight;
        public double scrollOffsetX, scrollOffsetY;
    };
}