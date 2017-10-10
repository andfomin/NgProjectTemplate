// Based on +https://github.com/aspnet/Proxy/blob/rel/2.0.0/src/Microsoft.AspNetCore.Proxy/ProxyMiddleware.cs

using Microsoft.AspNetCore.Http;
using System;
using System.Linq;
using System.Net.Http;
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
        private readonly HttpClient httpClient;
        private readonly NgProxyOptions options;

        public NgProxy(NgProxyOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.httpClient = new HttpClient(new HttpClientHandler());
        }

        public async Task HandleHttpRequest(HttpContext context)
        {
            var method = context.Request.Method;
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
            {
                var requestMessage = new HttpRequestMessage();
                // Copy the request headers
                foreach (var header in context.Request.Headers)
                {
                    requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
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

                    if (context.Response.StatusCode != StatusCodes.Status304NotModified)
                    {
                        await responseMessage.Content.CopyToAsync(context.Response.Body);
                    }
                }
            }
        }
    }
}
