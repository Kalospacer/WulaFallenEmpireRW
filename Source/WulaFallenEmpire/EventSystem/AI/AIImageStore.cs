using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Stores multimodal images (screenshots etc.) on disk and hands out lightweight references for
    /// conversation history. Keeping base64 out of the in-memory / persisted history avoids huge strings
    /// being copied, filtered and re-serialized on every turn.
    /// </summary>
    /// <remarks>
    /// History rows use the role "image" and the encoded form <c>img|&lt;fileName&gt;|&lt;w&gt;x&lt;h&gt;</c> — a few
    /// dozen bytes, persisted through the existing <see cref="AIHistoryManager"/> DTO. The actual base64
    /// needed to feed the model is transient: read from disk at the moment a tool result is produced,
    /// injected into that turn's messages only, and never written to history.
    /// </remarks>
    public static class AIImageStore
    {
        private const string RefPrefix = "img";
        private const char RefSep = '|';
        private static readonly Dictionary<string, Texture2D> _texCache = new Dictionary<string, Texture2D>();

        private static string GetImageDirectory()
        {
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "WulaAIImages");
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        /// <summary>Writes JPEG bytes to a new image file and returns its file name (not full path).</summary>
        public static string SaveImage(byte[] jpgBytes)
        {
            if (jpgBytes == null || jpgBytes.Length == 0) return null;
            try
            {
                string fileName = Guid.NewGuid().ToString("N") + ".jpg";
                File.WriteAllBytes(Path.Combine(GetImageDirectory(), fileName), jpgBytes);
                return fileName;
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[AIImageStore] SaveImage failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Reads an image file's raw bytes, or null if it cannot be read.</summary>
        public static byte[] LoadImageBytes(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            try
            {
                string path = Path.Combine(GetImageDirectory(), fileName);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[AIImageStore] LoadImageBytes failed for '" + fileName + "': " + ex.Message);
                return null;
            }
        }

        /// <summary>Loads (and caches) an image file as a texture, or null if unavailable.</summary>
        public static Texture2D LoadImageTexture(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return null;
            if (_texCache.TryGetValue(fileName, out var cached) && cached != null)
            {
                return cached;
            }
            byte[] bytes = LoadImageBytes(fileName);
            if (bytes == null || bytes.Length == 0) return null;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    return null;
                }
                _texCache[fileName] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[AIImageStore] LoadImageTexture failed for '" + fileName + "': " + ex.Message);
                return null;
            }
        }

        /// <summary>Whether the referenced image file currently exists.</summary>
        public static bool ImageExists(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            try
            {
                return File.Exists(Path.Combine(GetImageDirectory(), fileName));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Deletes an image file and drops it from the texture cache.</summary>
        public static void DeleteImage(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return;
            if (_texCache.TryGetValue(fileName, out var tex))
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
                _texCache.Remove(fileName);
            }
            try
            {
                string path = Path.Combine(GetImageDirectory(), fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                WulaLog.Debug("[AIImageStore] DeleteImage failed for '" + fileName + "': " + ex.Message);
            }
        }

        /// <summary>Destroys all cached textures (call when the dialog closes).</summary>
        public static void ClearCache()
        {
            foreach (var tex in _texCache.Values)
            {
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            _texCache.Clear();
        }

        /// <summary>Builds the history-row payload for an image reference.</summary>
        public static string BuildImageRef(string fileName, int width, int height)
        {
            return string.Concat(RefPrefix, RefSep.ToString(), fileName ?? string.Empty, RefSep.ToString(), width.ToString(), "x", height.ToString());
        }

        /// <summary>Parses an "image" history-row payload into its parts. Returns false when malformed.</summary>
        public static bool TryParseImageRef(string message, out string fileName, out int width, out int height)
        {
            fileName = null;
            width = 0;
            height = 0;
            if (string.IsNullOrWhiteSpace(message)) return false;
            string trimmed = message.Trim();
            if (!trimmed.StartsWith(RefPrefix + RefSep, StringComparison.Ordinal)) return false;
            string[] parts = trimmed.Split(RefSep);
            if (parts.Length < 3) return false;
            fileName = parts[1];
            string[] dims = parts[2].Split('x', 'X');
            if (dims.Length == 2)
            {
                int.TryParse(dims[0], out width);
                int.TryParse(dims[1], out height);
            }
            return !string.IsNullOrWhiteSpace(fileName);
        }
    }
}
