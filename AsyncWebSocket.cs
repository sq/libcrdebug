using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace crdebug {
    public abstract class AsyncWebSocket : IDisposable {
        protected abstract WebSocket BaseSocket { get; }

        public WebSocketCloseStatus? CloseStatus { get => BaseSocket.CloseStatus; }
        public string CloseStatusDescription { get => BaseSocket.CloseStatusDescription; }
        public string SubProtocol { get => BaseSocket.SubProtocol; }
        public WebSocketState State { get => BaseSocket.State; }

        public void Dispose () {
            BaseSocket.Dispose();
        }
    }

    public class AsyncClientWebSocket : AsyncWebSocket {
        public readonly ClientWebSocket Socket;

        private readonly SemaphoreSlim SendSemaphore = new SemaphoreSlim(1);
        private readonly SemaphoreSlim RecvSemaphore = new SemaphoreSlim(1);

        public ClientWebSocketOptions Options { get => Socket.Options; }

        public AsyncClientWebSocket () {
            Socket = new ClientWebSocket();
        }

        protected override WebSocket BaseSocket {
            get => Socket;
        }

        public void Abort () {
            Socket.Abort();
        }

        public Task ConnectAsync (Uri uri, CancellationToken cancellationToken) {
            return Socket.ConnectAsync(uri, cancellationToken);
        }

        public Task CloseAsync (WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) {
            return Socket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public Task CloseOutputAsync (WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) {
            return Socket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        }

        public async Task<WebSocketReceiveResult> ReceiveAsync (ArraySegment<byte> buffer, CancellationToken cancellationToken) {
            await RecvSemaphore.WaitAsync();
            try {
                return await Socket.ReceiveAsync(buffer, cancellationToken);
            } finally {
                RecvSemaphore.Release();
            }
        }

        public async Task SendAsync (ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) {
            await SendSemaphore.WaitAsync();
            try {
                await Socket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
            } finally {
                SendSemaphore.Release();
            }
        }
    }
}
