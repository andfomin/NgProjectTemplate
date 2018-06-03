using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AfominDotCom.AspNetCore.AngularCLI
{
    internal static class NgMiddlewareHelper
    {
        internal const string PackageJsonFileName = "package.json";
        internal const string AngularCliJsonFileName = ".angular-cli.json";
        internal const string AngularJsonFileName = "angular.json";

        internal const string ErrorAngularCliJsonNotFound = "No \".angular-cli.json\" or \"angular.json\" file was found in folder {0}.";
        internal const string ErrorAppsNotFound = "The \"apps\" element not found in .angular-cli.json .";
        private const string ErrorProjectsNotFound = "The \"projects\" element not found in angular.json .";
        internal const string ErrorNoBaseHrefInAngularCliJson = "A \"baseHref\" value is missing in apps[{0}] in .angular-cli.json .";
        internal const string ErrorNgSettingsNotFound = "Angular CLI settings are not found in the .angular-cli.json/angular.json file ";

        //-------

        internal class NgAppSettings
        {
            public int AppIndex;
            public string AppName;
            public string BaseHref;
            public string IndexFileName;
        }

        /// <summary>
        /// Reads settings from the "apps" array in .angular-cli.json.
        /// </summary>
        /// <param name="directory"></param>
        /// <returns></returns>
        internal static IEnumerable<NgAppSettings> GetAllNgAppSettings(string directory)
        {
            var angularCliJsonFilePath = Path.Combine(directory, NgMiddlewareHelper.AngularCliJsonFileName);
            var angularJsonFilePath = Path.Combine(directory, NgMiddlewareHelper.AngularJsonFileName);

            var ver1FileExists = File.Exists(angularCliJsonFilePath);
            var ver6FileExists = File.Exists(angularJsonFilePath);

            if (!ver1FileExists && !ver6FileExists)
            {
                throw new Exception(String.Format(ErrorAngularCliJsonNotFound, directory));
            }

            var fileText = File.ReadAllText(ver6FileExists ? angularJsonFilePath : angularCliJsonFilePath);
            var rootObj = JObject.Parse(fileText);

            IEnumerable<NgAppSettings> ngAppSettings = null;

            if (ver6FileExists)
            {
                var projectsObj = (JObject)rootObj["projects"];
                if (projectsObj == null)
                {
                    throw new Exception(ErrorProjectsNotFound);
                }

                ngAppSettings = projectsObj.Properties()
                    .Where(i => (i.Value is JObject) && i.HasValues)
                    .Select((i, index) => new
                    {
                        ProjectIndex = index,
                        ProjectName = i.Name,
                        OptionsObj = i.Value.SelectToken("architect.build.options"),
                    })
                    .Where(i => i.OptionsObj != null)
                    .Select(i => new NgAppSettings
                    {
                        AppIndex = i.ProjectIndex,
                        AppName = i.ProjectName,
                        BaseHref = (string)i.OptionsObj["baseHref"],
                        IndexFileName = (string)i.OptionsObj["index"],
                    })
                    .Where(i => i.BaseHref != null)
                    .ToList()
                    ;
            }
            else if (ver1FileExists)
            {
                var apps = (JArray)rootObj["apps"];
                if (apps == null)
                {
                    throw new Exception(ErrorAppsNotFound);
                }

                ngAppSettings = apps
                  .Select((i, index) => new NgAppSettings
                  {
                      AppIndex = index,
                      AppName = (string)i["name"],
                      BaseHref = (string)i["baseHref"],
                      IndexFileName = Path.GetFileName((string)i["index"]),
                  })
                  .ToList()
                  ;
            }

            if (ngAppSettings == null || !ngAppSettings.Any())
            {
                throw new Exception(ErrorNgSettingsNotFound);
            }
            return ngAppSettings;
        }


    }
}
