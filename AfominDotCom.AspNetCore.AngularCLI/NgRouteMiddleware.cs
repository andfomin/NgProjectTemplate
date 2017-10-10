using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    /// <summary>
    /// Serves the index file for a request path that starts with base-href specified in .angular-cli.json.
    /// </summary>
    public class NgRouteMiddleware
    {
        private const string ErrorNgIndexFileNotFound = "File {0} was not found.";

        private readonly RequestDelegate next;
        private List<KeyValuePair<string, string>> pathPrefixToFilePathMap;

        public NgRouteMiddleware(RequestDelegate next, IHostingEnvironment hostingEnv)
        {
            // Store the next middleware in the request processing chain.
            this.next = next ?? throw new ArgumentNullException(nameof(next));

            if (hostingEnv == null)
            {
                throw new ArgumentNullException(nameof(hostingEnv));
            }

            var contentRootPath = hostingEnv.ContentRootPath;
            var webRootPath = hostingEnv.WebRootPath;

            var ngAppSettings = NgMiddlewareHelper.GetAllNgAppSettings(contentRootPath)
              .Where(i => !String.IsNullOrWhiteSpace(i.BaseHref));

            if (!ngAppSettings.Any())
            {
                throw new Exception(String.Format(NgMiddlewareHelper.ErrorNoBaseHrefInAngularCliJson, 0));
            }

            this.pathPrefixToFilePathMap = ngAppSettings
              .Select(i => new
              {
            // Argument of PathString.StartsWithSegments() must not have a trailing slash.
            RequestPathSegment = i.BaseHref == "/" ? i.BaseHref : i.BaseHref.TrimEnd('/'),
            // Slashes are acceptable in the middle and at the end, but not at the start of a path segment.
            IndexFilePath = Path.Combine(webRootPath, i.BaseHref.TrimStart('/'), i.IndexFileName),
              })
              .Select(i => new KeyValuePair<string, string>(i.RequestPathSegment, i.IndexFilePath))
              .ToList()
              ;
            this.pathPrefixToFilePathMap.ForEach(i =>
            {
                if (!File.Exists(i.Value))
                {
                    throw new Exception(String.Format(ErrorNgIndexFileNotFound, i.Value));
                }
            });
        }

        /// <summary>
        /// Processes a request to determine if it matches a known baseHref, and if so, serves the corresponding index file.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public Task Invoke(HttpContext context)
        {
            if (HttpMethods.IsGet(context.Request.Method))
            {
                var requestPath = context.Request.Path;
                // Bypass requests for files. They must be served by a preceeding UseStaticFiles()/StaticFileMiddleware .
                var lastSegmentStartPos = requestPath.Value.LastIndexOf('/');
                var hasDotInLastSegment = requestPath.Value.IndexOf('.', lastSegmentStartPos + 1) >= 0;

                if (!hasDotInLastSegment)
                {
                    var indexFilePath = this.pathPrefixToFilePathMap
                      // If baseHref is "/", that means catch all.
                      .Where(i => (i.Key == "/") || requestPath.StartsWithSegments(i.Key))
                      .Select(i => i.Value)
                      .FirstOrDefault()
                      ;
                    if (!String.IsNullOrEmpty(indexFilePath) && File.Exists(indexFilePath))
                    {
                        context.Response.ContentType = "text/html";
                        return context.Response.SendFileAsync(indexFilePath);
                    }
                }
            }

            return this.next.Invoke(context);
        }
    }
}
