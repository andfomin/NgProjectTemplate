// Based on +https://github.com/aspnet/Proxy/blob/rel/2.0.0/src/Microsoft.AspNetCore.Proxy/ProxyMiddleware.cs

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    internal class NgProxyOptions
    {
        public string Scheme { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
    }

    internal class NgProxy
    {
        private static readonly string[] NotForwardedWebSocketHeaders =
          new[] { "Connection", "Host", "Upgrade", "Sec-WebSocket-Key", "Sec-WebSocket-Version" };
        private const int DefaultWebSocketBufferSize = 4096;

        private readonly HttpClient httpClient;
        private NgProxyOptions options;

        public NgProxy(NgProxyOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.httpClient = new HttpClient(new HttpClientHandler());
        }

        /// <summary>
        /// Forward requests to another server according to Options.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task HandleRequest(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest)
            {
                await HandleWebSocketRequest(context);
            }
            else
            {
                await HandleHttpRequest(context);
            }
        }

        private async Task HandleHttpRequest(HttpContext context)
        {
            var requestMessage = new HttpRequestMessage();

            var requestMethod = context.Request.Method;
            if (!HttpMethods.IsGet(requestMethod) &&
                !HttpMethods.IsHead(requestMethod) &&
                !HttpMethods.IsDelete(requestMethod) &&
                !HttpMethods.IsTrace(requestMethod))
            {
                requestMessage.Content = new StreamContent(context.Request.Body);
            }

            foreach (var header in context.Request.Headers)
            {
                if (!requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()) && requestMessage.Content != null)
                {
                    requestMessage.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
                }
            }
            requestMessage.Headers.Host = this.options.Host + ":" + this.options.Port;

            var uriString = $"{this.options.Scheme}://{this.options.Host}:{this.options.Port}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
            requestMessage.RequestUri = new Uri(uriString);
            requestMessage.Method = new HttpMethod(context.Request.Method);

            var responseMessage = await this.httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            using (responseMessage)
            {
                context.Response.StatusCode = (int)responseMessage.StatusCode;

                var responseHeaders = responseMessage.Headers.Concat(responseMessage.Content.Headers);
                foreach (var header in responseHeaders)
                {
                    context.Response.Headers[header.Key] = header.Value.ToArray();
                }

                // SendAsync removes chunking from the response. This removes the header so it doesn't expect a chunked response.
                context.Response.Headers.Remove(HeaderNames.TransferEncoding);

                if (context.Response.StatusCode != StatusCodes.Status304NotModified)
                {
                    await responseMessage.Content.CopyToAsync(context.Response.Body);
                }
            }
        }

        private async Task HandleWebSocketRequest(HttpContext context)
        {
            using (var client = new ClientWebSocket())
            {
                foreach (var headerEntry in context.Request.Headers)
                {
                    if (!NotForwardedWebSocketHeaders.Contains(headerEntry.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        client.Options.SetRequestHeader(headerEntry.Key, headerEntry.Value);
                    }
                }

                var wsScheme = string.Equals(this.options.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws";
                var uriString = $"{wsScheme}://{this.options.Host}:{this.options.Port}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";

                client.Options.KeepAliveInterval = TimeSpan.FromMinutes(1);

                try
                {
                    await client.ConnectAsync(new Uri(uriString), context.RequestAborted);
                }
                catch (WebSocketException)
                {
                    context.Response.StatusCode = 400;
                    return;
                }

                using (var server = await context.WebSockets.AcceptWebSocketAsync(client.SubProtocol))
                {
                    await Task.WhenAll(
                      PumpWebSocket(client, server, context.RequestAborted),
                      PumpWebSocket(server, client, context.RequestAborted));
                }
            }
        }

        private async Task CloseWebSocketOutput(WebSocket webSocket, WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            // If the websocket is not in an appropriate state, an exception will be thrown.
            var canClose = (webSocket.State == WebSocketState.Open) || (webSocket.State == WebSocketState.CloseReceived);
            if (canClose)
            {
                await webSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
            }
        }

        private async Task PumpWebSocket(WebSocket source, WebSocket destination, CancellationToken cancellationToken)
        {
            var buffer = new byte[DefaultWebSocketBufferSize];
            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await source.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await CloseWebSocketOutput(destination, WebSocketCloseStatus.EndpointUnavailable, null, cancellationToken);
                    return;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseWebSocketOutput(destination, source.CloseStatus.Value, source.CloseStatusDescription, cancellationToken);
                    return;
                }

                await destination.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count), result.MessageType, result.EndOfMessage, cancellationToken);
            }
        }

    }
}
