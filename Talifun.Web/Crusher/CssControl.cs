﻿using System;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using System.Web.UI;
using System.Web.UI.WebControls;
using Talifun.Web.Crusher.Config;

namespace Talifun.Web.Crusher
{
    /// <summary>
    /// Generates the include references to css for a web page according to the provided configuration.
    /// </summary>
    public class CssControl : WebControl
    {
        protected readonly string QuerystringKeyName;
        protected readonly CssGroupElementCollection CssGroups;
        protected readonly IRetryableFileOpener RetryableFileOpener;
        protected readonly IHasher Hasher;
        protected static string CssControlType = typeof(CssControl).ToString();

        public CssControl()
        {
            QuerystringKeyName = CurrentCrusherConfiguration.Current.QuerystringKeyName;
            CssGroups = CurrentCrusherConfiguration.Current.CssGroups;
            RetryableFileOpener = new RetryableFileOpener();
            Hasher = new Hasher(RetryableFileOpener);
        }

        /// <summary>
        /// The name of css group to generate the include headers for.
        /// </summary>
        public virtual string GroupName
        {
            get
            {
                var o = ViewState["GroupName"];
                return ((o == null) ? String.Empty : (string)o);
            }
            set
            {
                ViewState["GroupName"] = value;
            }
        }

        /// <summary>
        /// Generate the url for the crushed css file.
        /// </summary>
        /// <param name="writer"></param>
        /// <remarks>
        /// The generated url will also have a querystring with the hash of the file appended to it.
        /// </remarks>
        protected override void Render(HtmlTextWriter writer)
        {
            var cssGroup = CssGroups[GroupName];
            var outputFilePath = cssGroup.OutputFilePath;
            var scriptLinks = string.Empty;

            var cacheKey = GetKey(outputFilePath);

            var cachedValue = HttpRuntime.Cache.Get(cacheKey);
            if (cachedValue != null)
            {
                scriptLinks = (string)cachedValue;
            }
            else
            {
                if (!cssGroup.Debug)
                {
                    var fileInfo = new FileInfo(HostingEnvironment.MapPath(outputFilePath));
                    var etag = Hasher.CalculateMd5Etag(fileInfo);
                    var url = string.IsNullOrEmpty(cssGroup.Url) ? this.ResolveUrl(outputFilePath) : cssGroup.Url;

                    scriptLinks = "<link rel=\"stylesheet\" type=\"text/css\" href=\"" + url + "?" + QuerystringKeyName + "=" + etag + "\" media=\"" + cssGroup.Media + "\" />"; 
                }
                else
                {
                    var scriptLinksBuilder = new StringBuilder();
                    foreach (CssFileElement file in cssGroup.Files)
                    {
                        var fileInfo = new FileInfo(HostingEnvironment.MapPath(file.FilePath));
                        var etag = Hasher.CalculateMd5Etag(fileInfo);
                        var url = this.ResolveUrl(file.FilePath);

                        var fileName = fileInfo.Name.ToLower();

                        if (fileName.EndsWith(".less") || fileName.EndsWith(".less.css"))
                        {
                            etag = "'" + etag + "'";
                        }

                        scriptLinksBuilder.Append("<link rel=\"stylesheet\" type=\"text/css\" href=\"" + url + "?" + QuerystringKeyName + "=" + etag + "\" media=\"" + cssGroup.Media + "\" />"); 
                    }
                    scriptLinks = scriptLinksBuilder.ToString();
                }
                AddToCache(outputFilePath, scriptLinks);
            }

            writer.Write(scriptLinks);
            return;
        }

        /// <summary>
        /// Cache the url generated for the crushed css file.
        /// </summary>
        /// <param name="outputFilePath">The crushed css file to use as the cache key.</param>
        /// <param name="scriptLinks">The link generated for the crushed file.</param>
        protected void AddToCache(string outputFilePath, string scriptLinks)
        {
            var cacheKey = GetKey(outputFilePath);
            var filePath = this.MapPathSecure(outputFilePath);

            HttpRuntime.Cache.Insert(
                cacheKey,
                scriptLinks,
                new CacheDependency(filePath, DateTime.Now),
                Cache.NoAbsoluteExpiration,
                Cache.NoSlidingExpiration);
        }

        /// <summary>
        /// Get the cache key to use for caching.
        /// </summary>
        /// <param name="outputPath">The path for the crushed css file.</param>
        /// <returns>The cache key to use for caching.</returns>
        private static string GetKey(string outputPath)
        {
            var prefix = CssControlType + "|";
            return prefix + outputPath;
        }
    }
}