/*
' Copyright (c) 2015-2016 Satrabel.be
'  All rights reserved.
' 
' THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
' TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
' THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
' CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
' DEALINGS IN THE SOFTWARE.
' 
*/

using System.Collections.Generic;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Common.Utilities;
using System.Xml;
using System.Linq;
using DotNetNuke.Common;
using System;
using DotNetNuke.Services.Search.Entities;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using DotNetNuke.Entities.Portals;
using System.IO;
using System.Web.Hosting;
using Satrabel.OpenContent.Components.Json;
using Satrabel.OpenContent.Components.TemplateHelpers;

namespace Satrabel.OpenContent.Components
{
    public class FeatureController : ModuleSearchBase, IPortable, IUpgradeable
    {
        #region Optional Interfaces
        public string ExportModule(int ModuleID)
        {
            string xml = "";
            OpenContentController ctrl = new OpenContentController();
            var content = ctrl.GetFirstContent(ModuleID);
            if ((content != null))
            {
                xml += "<opencontent>";
                xml += "<json>" + XmlUtils.XMLEncode(content.Json) + "</json>";
                xml += "</opencontent>";
            }
            return xml;
        }
        public void ImportModule(int ModuleID, string Content, string Version, int UserID)
        {
            OpenContentController ctrl = new OpenContentController();
            XmlNode xml = Globals.GetContent(Content, "opencontent");
            var content = new OpenContentInfo()
            {
                ModuleId = ModuleID,
                Json = xml.SelectSingleNode("json").InnerText,
                CreatedByUserId = UserID,
                CreatedOnDate = DateTime.Now,
                LastModifiedByUserId = UserID,
                LastModifiedOnDate = DateTime.Now,
                Title = "",
                Html = ""
            };
            ctrl.AddContent(content, false, null);
        }
        #region ModuleSearchBase
        public override IList<SearchDocument> GetModifiedSearchDocuments(ModuleInfo modInfo, DateTime beginDateUtc)
        {
            var searchDocuments = new List<SearchDocument>();

            //If module is marked as "don't index" then return no results
            if (modInfo.ModuleSettings.GetValue("AllowIndex", "True") == "False")
                return searchDocuments;

            //If tab of the module is marked as "don't index" then return no results
            if (modInfo.ParentTab.TabSettings.GetValue("AllowIndex", "True") == "False")
                return searchDocuments;

            //If tab is marked as "inactive" then return no results
            if (modInfo.ParentTab.DisableLink)
                return searchDocuments;

            OpenContentSettings settings = new OpenContentSettings(modInfo.ModuleSettings);
            if (!settings.Template.Main.DnnSearch)
            {
                return searchDocuments;
            }

            OpenContentController ctrl = new OpenContentController();
            OpenContentInfo content = ctrl.GetFirstContent(modInfo.ModuleID);
            if (content != null &&
                (content.LastModifiedOnDate.ToUniversalTime() > beginDateUtc &&
                 content.LastModifiedOnDate.ToUniversalTime() < DateTime.UtcNow))
            {
                Log.Logger.DebugFormat("Indexing content {0}-{1} ({2}) {3} versus {4}", modInfo.ModuleID, modInfo.ModuleTitle, modInfo.TabID, beginDateUtc, content.LastModifiedOnDate.ToUniversalTime());

                SearchDocument searchDoc;
                if (DnnLanguageUtils.IsMultiLingualPortal(modInfo.PortalID))
                {
                    string culture = modInfo.CultureCode;
                    JToken title;
                    JToken description;
                    if (content.Title.IsJson())
                    {
                        JToken singleLanguage = content.JsonAsJToken;
                        JsonUtils.SimplifyJson(singleLanguage, culture);
                        title = singleLanguage["Title"] ?? modInfo.ModuleTitle;
                        description = singleLanguage["Description"] ?? JsonToSearchableString(content.Json);
                    }
                    else
                    {
                        title = content.Title;
                        description = JsonToSearchableString(content.Json);
                    }
                    searchDoc = CreateSearchDocument(modInfo, culture, title.ToString(), description.ToString(), content.LastModifiedOnDate.ToUniversalTime());
                    searchDocuments.Add(searchDoc);
                }
                else
                {
                    searchDoc = CreateSearchDocument(modInfo, "", content.Title, JsonToSearchableString(content.Json), content.LastModifiedOnDate.ToUniversalTime());
                    searchDocuments.Add(searchDoc);
                }
            }
            return searchDocuments;
        }

        private static SearchDocument CreateSearchDocument(ModuleInfo modInfo, string culture, string title, string body, DateTime time)
        {
            return new SearchDocument
              {
                  UniqueKey = modInfo.ModuleID + "-" + culture,
                  PortalId = modInfo.PortalID,
                  Title = modInfo.ModuleTitle.StripHtml(),
                  Description = title.StripHtml(),
                  Body = body.StripHtml(),
                  ModifiedTimeUtc = time,
                  CultureCode = culture
              };
        }

        protected static string JsonToSearchableString(string json)
        {
            dynamic data = JToken.Parse(json);
            string result = JsonToSearchableString(data);
            return result;
        }

        protected static string JsonToSearchableString(JToken data)
        {
            string tagPattern = @"<(.|\n)*?>";
            string result = "";

            if (data.Type == JTokenType.Object)
            {
                foreach (JProperty item in data.Children<JProperty>())
                {
                    result += JsonToSearchableString(item);
                }
            }
            else if (data.Type == JTokenType.Array)
            {
                foreach (JToken item in data.Children())
                {
                    result += JsonToSearchableString(item);
                }
            }
            else if (data.Type == JTokenType.Property)
            {
                var item = (JProperty)data;

                if (item.Value.Type == JTokenType.String)
                {
                    //result += Regex.Replace(item.Value.ToString(), tagPattern, string.Empty) + " ... ";
                    result += data.ToString().StripHtml() + " ... ";
                }
                else
                {
                    result += JsonToSearchableString(item.Value);
                }
            }
            else
            {
                //result += Regex.Replace(data.ToString(), tagPattern, string.Empty) + " ... ";
                result += data.ToString().StripHtml() + " ... ";
            }

            return result;
        }

        #endregion
        /// -----------------------------------------------------------------------------
        /// <summary>
        /// UpgradeModule implements the IUpgradeable Interface
        /// </summary>
        /// <param name="Version">The current version of the module</param>
        /// -----------------------------------------------------------------------------
        //public string UpgradeModule(string Version)
        //{
        //	throw new System.NotImplementedException("The method or operation is not implemented.");
        //}
        #endregion

        public string UpgradeModule(string Version)
        {
            string res = "";
            if (Version == "02.01.00")
            {

                var pc = new PortalController();
                foreach (var p in pc.GetPortals().Cast<PortalInfo>())
                {
                    string webConfig = HostingEnvironment.MapPath("~/" + p.HomeDirectory + "/OpenContent/Templates/web.config");
                    res += webConfig;
                    if (File.Exists(webConfig))
                    {
                        res += " : found \n";
                        File.Delete(webConfig);
                        string filename = HostingEnvironment.MapPath("~/DesktopModules/OpenContent/Templates/web.config");
                        File.Copy(filename, webConfig);
                    }
                }
            }
            return Version + res;
        }
    }
}
