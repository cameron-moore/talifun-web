﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web;
using System.Web.Caching;
using Talifun.Web.Helper;
using Yahoo.Yui.Compressor;

namespace Talifun.Web.Crusher
{
    /// <summary>
    /// Manages the adding and removing of js files to crush. It also does the js crushing.
    /// </summary>
    public class JsCrusher : IJsCrusher
    {
        protected readonly IRetryableFileOpener RetryableFileOpener;
        protected readonly IRetryableFileWriter RetryableFileWriter;
        protected readonly IPathProvider PathProvider;
        protected static string JsCrusherType = typeof(JsCrusher).ToString();

        public JsCrusher(IRetryableFileOpener retryableFileOpener, IRetryableFileWriter retryableFileWriter, IPathProvider pathProvider)
        {
            RetryableFileOpener = retryableFileOpener;
            RetryableFileWriter = retryableFileWriter;
            PathProvider = pathProvider;
        }

        /// <summary>
        /// Add js files to be crushed
        /// </summary>
        /// <param name="outputUri">The virtual path for the crushed js file</param>
        /// <param name="files">The js files to be crushed</param>
        /// 
        public virtual void AddFiles(Uri outputUri, IEnumerable<JsFile> files)
        {
            var crushedContent = ProcessFiles(files);
            var outputFileInfo = new FileInfo(PathProvider.MapPath(outputUri));
            RetryableFileWriter.SaveContentsToFile(crushedContent, outputFileInfo);
            AddFilesToCache(outputUri, files);
        }

        /// <summary>
        /// Compress the js files and store them in the specified js file.
        /// </summary>
        /// <param name="files">The js files to be crushed.</param>
        public virtual StringBuilder ProcessFiles(IEnumerable<JsFile> files)
        {
            var uncompressedContents = new StringBuilder();
            var toBeCompressedContents = new StringBuilder();

            foreach (var file in files)
            {
                var filePath = PathProvider.MapPath(file.FilePath);
                var fileInfo = new FileInfo(filePath);
                var fileContents = RetryableFileOpener.ReadAllText(fileInfo);

                switch (file.CompressionType)
                {
                    case JsCompressionType.None:
                        uncompressedContents.AppendLine(fileContents);
                        break;
                    case JsCompressionType.Min:
                        toBeCompressedContents.AppendLine(fileContents);
                        break;
                }
            }

            if (toBeCompressedContents.Length > 0)
            {
                uncompressedContents.Append(JavaScriptCompressor.Compress(toBeCompressedContents.ToString()));
            }

            return uncompressedContents;
        }

        /// <summary>
        /// Remove all js files from being crushed
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file</param>
        public virtual void RemoveFiles(Uri outputUri)
        {
            HttpRuntime.Cache.Remove(GetKey(outputUri));
        }

        /// <summary>
        /// Add the js files to the cache so that they are monitored for any changes.
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file.</param>
        /// <param name="jsFiles">The js files to be crushed.</param>
        public virtual void AddFilesToCache(Uri outputUri, IEnumerable<JsFile> jsFiles)
        {
            var fileNames = new List<string>
                                {
                                     PathProvider.MapPath(outputUri)
                                };

            foreach (var file in jsFiles)
            {
                fileNames.Add(PathProvider.MapPath(file.FilePath));
            }

            var jsCacheItem = new JsCacheItem()
                                  {
                                      OutputUri = outputUri,
                                      JsFiles = jsFiles
                                  };

            HttpRuntime.Cache.Insert(
                GetKey(outputUri),
                jsCacheItem,
                new CacheDependency(fileNames.ToArray(), System.DateTime.Now),
                Cache.NoAbsoluteExpiration,
                Cache.NoSlidingExpiration,
                CacheItemPriority.High,
                FileRemoved);
        }

        /// <summary>
        /// When a file is removed from cache, keep it in the cache if it is unused or expired as we want to continue to monitor
        /// any changes to file. If it has been removed because the file has changed then regenerate the crushed js file and
        /// start the monitoring again.
        /// </summary>
        /// <param name="key">The key of the cache item</param>
        /// <param name="value">The value of the cache item</param>
        /// <param name="reason">The reason the file was removed from cache.</param>
        public virtual void FileRemoved(string key, object value, CacheItemRemovedReason reason)
        {
            var jsCacheItem = (JsCacheItem)value;
            switch (reason)
            {
                case CacheItemRemovedReason.DependencyChanged:
                    AddFiles(jsCacheItem.OutputUri, jsCacheItem.JsFiles);
                    break;
                case CacheItemRemovedReason.Underused:
                case CacheItemRemovedReason.Expired:
                    AddFilesToCache(jsCacheItem.OutputUri, jsCacheItem.JsFiles);
                    break;
            }
        }

        /// <summary>
        /// Get the cache key to use for caching.
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file.</param>
        /// <returns>The cache key to use for caching.</returns>
        public virtual string GetKey(Uri outputUri)
        {
            var prefix = JsCrusherType + "|";
            return prefix + outputUri;
        }
    }
}