﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Caching;
using System.Web.Hosting;
using Talifun.Web.Helper;

namespace Talifun.Web.CssSprite
{
    /// <summary>
    /// Manages the creation of css sprites images.
    /// </summary>
    public class CssSpriteCreator : ICssSpriteCreator
    {
        protected readonly int BufferSize = 32768;
        protected readonly int ImagePadding = 2;

        protected readonly IRetryableFileOpener RetryableFileOpener;
        protected readonly IHasher Hasher;
        protected readonly IRetryableFileWriter RetryableFileWriter;

        protected static string CssSpriteCreatorType = typeof(CssSpriteCreator).ToString();

        public CssSpriteCreator()
        {
            RetryableFileOpener = new RetryableFileOpener();
            Hasher = new Hasher(RetryableFileOpener);
            RetryableFileWriter = new RetryableFileWriter(BufferSize, RetryableFileOpener, Hasher);
        }

        /// <summary>
        /// Add images to be generated into sprite image.
        /// </summary>
        /// <param name="imageOutputPath">Sprite image output path.</param>
        /// <param name="spriteImageUrl">Sprite image url.</param>
        /// <param name="cssOutputPath">Sprite css output path.</param>
        /// <param name="files">The component images for the sprite.</param>
        public virtual void AddFiles(FileInfo imageOutputPath, Uri spriteImageUrl, FileInfo cssOutputPath, IEnumerable<ImageFile> files)
        {
            var spriteElements = ProcessFiles(files);
            var etag = SaveSpritesImage(spriteElements, imageOutputPath);
            var cssSpriteImageUrl = new Uri(string.IsNullOrEmpty(spriteImageUrl.OriginalString) ? VirtualPathUtility.ToAbsolute(imageOutputPath.FullName) : spriteImageUrl.OriginalString);
            var css = GetCssSpriteCss(spriteElements, etag, cssSpriteImageUrl);
            RetryableFileWriter.SaveContentsToFile(css, cssOutputPath);
            AddFilesToCache(imageOutputPath, spriteImageUrl, cssOutputPath, files);
        }

        /// <summary>
        /// Get sprites for image files.
        /// </summary>
        /// <param name="files">The component images for the sprite.</param>
        /// <returns>A list of css sprites</returns>
        public virtual List<SpriteElement> ProcessFiles(IEnumerable<ImageFile> files)
        {
            var spriteElements = new List<SpriteElement>();
            foreach (var file in files)
            {
                var filePath = HostingEnvironment.MapPath(file.FilePath);
                var fileInfo = new FileInfo(filePath);
                using (var reader = RetryableFileOpener.OpenFileStream(fileInfo, 5, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var spriteElement = new SpriteElement(file.Name, reader);
                    spriteElements.Add(spriteElement);
                }
            }

            return spriteElements;
        }

        /// <summary>
        /// Save sprites image.
        /// </summary>
        /// <param name="spriteElements">The sprite elements to save</param>
        /// <param name="imageOutputPath">The location to save the image too.</param>
        /// <returns>etag of generated sprite file</returns>
        public virtual string SaveSpritesImage(List<SpriteElement> spriteElements, FileInfo imageOutputPath)
        {
            using (var image = GetCssSpriteImage(spriteElements))
            {
                using (var writer = new MemoryStream())
                {
                    image.Save(writer, ImageFormat.Png);
                    writer.Flush();

                    return RetryableFileWriter.SaveContentsToFile(writer, imageOutputPath);
                }
            }
        }

        /// <summary>
        /// Add the images to the cache so that they are monitored for any changes.
        /// </summary>
        /// <param name="imageOutputPath">Sprite image output path.</param>
        /// <param name="spriteImageUrl">Sprite image url.</param>
        /// <param name="cssOutputPath">Sprite css output path.</param>
        /// <param name="files">The component images for the sprite.</param>
        public virtual void AddFilesToCache(FileInfo imageOutputPath, Uri spriteImageUrl, FileInfo cssOutputPath, IEnumerable<ImageFile> files)
        {
            var fileNames = new List<string>
                                {
                                    imageOutputPath.FullName,
                                    cssOutputPath.FullName
                                };

            foreach (var file in files)
            {
                fileNames.Add(HostingEnvironment.MapPath(file.FilePath));
            }

            var cssSpriteCacheItem = new CssSpriteCacheItem()
            {
                ImageFiles = files,
                CssOutputPath = cssOutputPath,
                ImageOutputPath = imageOutputPath,
                SpriteImageUrl = spriteImageUrl
            };

            HttpRuntime.Cache.Insert(
                GetKey(imageOutputPath, spriteImageUrl, cssOutputPath),
                cssSpriteCacheItem,
                new CacheDependency(fileNames.ToArray(), System.DateTime.Now),
                Cache.NoAbsoluteExpiration,
                Cache.NoSlidingExpiration,
                CacheItemPriority.High,
                FileRemoved);
        }

        /// <summary>
        /// When a file is removed from cache, keep it in the cache if it is unused or expired as we want to continue to monitor
        /// any changes to file. If it has been removed because the file has changed then regenerate the sprite image and
        /// start the monitoring again.
        /// </summary>
        /// <param name="key">The key of the cache item.</param>
        /// <param name="value">The value of the cache item.</param>
        /// <param name="reason">The reason the file was removed from cache.</param>
        public virtual void FileRemoved(string key, object value, CacheItemRemovedReason reason)
        {
            var cssSpriteCacheItem = (CssSpriteCacheItem)value;

            switch (reason)
            {
                case CacheItemRemovedReason.DependencyChanged:
                    AddFiles(cssSpriteCacheItem.ImageOutputPath, cssSpriteCacheItem.SpriteImageUrl, cssSpriteCacheItem.CssOutputPath, cssSpriteCacheItem.ImageFiles);
                    break;
                case CacheItemRemovedReason.Underused:
                case CacheItemRemovedReason.Expired:
                    AddFilesToCache(cssSpriteCacheItem.ImageOutputPath, cssSpriteCacheItem.SpriteImageUrl, cssSpriteCacheItem.CssOutputPath, cssSpriteCacheItem.ImageFiles);
                    break;
            }
        }

        /// <summary>
        /// Remove sprite image from cache.
        /// </summary>
        /// <param name="imageOutputPath">Sprite image output path.</param>
        /// <param name="spriteImageUrl">Sprite image url.</param>
        /// <param name="cssOutputPath">Sprite css output path.</param>
        public virtual void RemoveFiles(FileInfo imageOutputPath, Uri spriteImageUrl, FileInfo cssOutputPath)
        {
            HttpRuntime.Cache.Remove(GetKey(imageOutputPath, spriteImageUrl, cssOutputPath));
        }

        /// <summary>
        /// Get the cache key to use.
        /// </summary>
        /// <param name="imageOutputPath">Sprite image output path.</param>
        /// <param name="spriteImageUrl">Sprite image url.</param>
        /// <param name="cssOutputPath">Sprite css output path.</param>
        /// <returns></returns>
        public virtual string GetKey(FileInfo imageOutputPath, Uri spriteImageUrl, FileInfo cssOutputPath)
        {
            var prefix = CssSpriteCreatorType + "|";
            return prefix + imageOutputPath + "|" + spriteImageUrl + "|" + cssOutputPath;
        }

        /// <summary>
        /// Generate the css that denotes the composite sprite part locations and dimensions.
        /// </summary>
        /// <param name="spriteElements">The sprites that make the image up.</param>
        /// <param name="etag">The unique hash for the sprite image.</param>
        /// <param name="cssSpriteImageUrl">The url of the sprite image.</param>
        /// <returns></returns>
        public virtual string GetCssSpriteCss(IEnumerable<SpriteElement> spriteElements, string etag, Uri cssSpriteImageUrl)
        {
            var cssBuilder = new StringBuilder();
            var currentY = 0;
            foreach (var element in spriteElements)
            {
                cssBuilder.AppendFormat(".{0} {{background-image: url('{1}');background-position: 0px -{2}px;width: {3}px;height: {4}px;}}", element.Name, cssSpriteImageUrl + "?" + etag, currentY, element.Width, element.Height);
                currentY += element.Height + ImagePadding;
            }

            return cssBuilder.ToString();
        }

        /// <summary>
        /// Generate image from composite sprite parts.
        /// </summary>
        /// <param name="spriteElements">The sprites that make the image up.</param>
        /// <returns>Image the represents all the sprites.</returns>
        public virtual Bitmap GetCssSpriteImage(IEnumerable<SpriteElement> spriteElements)
        {
            var maxWidth = MaxWidth(spriteElements);
            var maxHeight = TotalHeight(spriteElements);

            var sprite = new Bitmap(maxWidth, maxHeight);
            var graphic = Graphics.FromImage(sprite);

            var currentY = 0;
            foreach (var element in spriteElements)
            {
                graphic.DrawImage(element.Image, 0, currentY, element.Width, element.Height);
                currentY += element.Height + ImagePadding;
            }

            return sprite;
        }

        /// <summary>
        /// Get the height of all the sprites added together.
        /// </summary>
        /// <param name="spriteElements">The sprites that make the image up.</param>
        /// <returns>The height if all the sprites added together.</returns>
        public virtual int TotalHeight(IEnumerable<SpriteElement> spriteElements)
        {
            return spriteElements.Sum(x => x.Height + ImagePadding);
        }

        /// <summary>
        /// Get the width of the widest sprite.
        /// </summary>
        /// <param name="spriteElements">The sprites that make the image up.</param>
        /// <returns>The width of the widest sprite.</returns>
        public virtual int MaxWidth(IEnumerable<SpriteElement> spriteElements)
        {
            return spriteElements.Max(x => x.Width);
        }
    }
}
