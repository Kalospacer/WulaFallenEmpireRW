using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.Utils
{
    public static class DefInjectedExportUtility
    {
        private sealed class InjectionValue
        {
            public string Key;
            public bool IsCollection;
            public List<string> Values;
        }

        public static void ExportDefInjectedTemplateFromDefs(ModContentPack content)
        {
            try
            {
                if (content?.ModMetaData == null)
                {
                    Messages.Message("Export failed: Mod content metadata not found.", MessageTypeDefOf.RejectInput);
                    return;
                }

                string outRoot = Path.Combine(
                    GenFilePaths.SaveDataFolderPath,
                    "WulaFallenEmpire_DefInjectedExport",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss"));

                string outDefInjected = Path.Combine(outRoot, "English", "DefInjected");
                Directory.CreateDirectory(outDefInjected);

                string outTsvPath = Path.Combine(outRoot, "worklist.tsv");

                var entriesByFolder = new Dictionary<string, Dictionary<string, InjectionValue>>(StringComparer.OrdinalIgnoreCase);
                var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                List<Type> defTypes = GenTypes.AllSubclassesNonAbstract(typeof(Def)).ToList();
                defTypes.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));

                foreach (Type defType in defTypes)
                {
                    DefInjectionUtility.ForEachPossibleDefInjection(
                        defType,
                        (string suggestedPath,
                            string normalizedPath,
                            bool isCollection,
                            string currentValue,
                            IEnumerable<string> currentValueCollection,
                            bool translationAllowed,
                            bool fullListTranslationAllowed,
                            FieldInfo fieldInfo,
                            Def def) =>
                        {
                            if (!translationAllowed)
                            {
                                return;
                            }

                            if (string.IsNullOrWhiteSpace(suggestedPath))
                            {
                                return;
                            }

                            if (suggestedPath.IndexOf(".modContentPack.", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return;
                            }

                            if (!isCollection && string.Equals(fieldInfo?.Name, "defName", StringComparison.OrdinalIgnoreCase))
                            {
                                return;
                            }

                            List<string> values;
                            bool collectionOut;
                            if (isCollection)
                            {
                                values = currentValueCollection?.Where(v => KeepValue(suggestedPath, fieldInfo, v)).ToList() ?? new List<string>();
                                if (values.Count == 0)
                                {
                                    return;
                                }
                                collectionOut = true;
                            }
                            else
                            {
                                if (!KeepValue(suggestedPath, fieldInfo, currentValue))
                                {
                                    return;
                                }
                                values = new List<string> { currentValue };
                                collectionOut = false;
                            }

                            if (!seenKeys.Add(suggestedPath))
                            {
                                return;
                            }

                            string folderName = GetDefInjectedFolderName(def?.GetType() ?? defType);
                            if (!entriesByFolder.TryGetValue(folderName, out Dictionary<string, InjectionValue> folderEntries))
                            {
                                folderEntries = new Dictionary<string, InjectionValue>(StringComparer.OrdinalIgnoreCase);
                                entriesByFolder.Add(folderName, folderEntries);
                            }

                            folderEntries.Add(
                                suggestedPath,
                                new InjectionValue
                                {
                                    Key = suggestedPath,
                                    IsCollection = collectionOut,
                                    Values = values
                                });
                        },
                        content.ModMetaData);
                }

                WriteDefInjectedXmlOutputs(entriesByFolder, outDefInjected);
                WriteWorklistTsv(entriesByFolder, outTsvPath);

                Messages.Message($"DefInjected export written to: {outRoot}", MessageTypeDefOf.TaskCompletion);
            }
            catch (Exception ex)
            {
                Log.Error($"[WulaFallenEmpire] DefInjected export failed: {ex}");
                Messages.Message("DefInjected export failed (see log).", MessageTypeDefOf.RejectInput);
            }
        }

        private static void WriteDefInjectedXmlOutputs(
            Dictionary<string, Dictionary<string, InjectionValue>> entriesByFolder,
            string outDefInjectedRoot)
        {
            foreach (KeyValuePair<string, Dictionary<string, InjectionValue>> folderPair in entriesByFolder)
            {
                string folder = folderPair.Key;
                Dictionary<string, InjectionValue> entries = folderPair.Value;
                if (entries.Count == 0)
                {
                    continue;
                }

                string safeFolder = SanitizePathSegment(folder);
                string folderPath = Path.Combine(outDefInjectedRoot, safeFolder);
                Directory.CreateDirectory(folderPath);

                WriteXmlUtf8Indented(
                    BuildLanguageDataDocument(entries, todoMode: false),
                    Path.Combine(folderPath, "Auto_CN.xml"));

                WriteXmlUtf8Indented(
                    BuildLanguageDataDocument(entries, todoMode: true),
                    Path.Combine(folderPath, "Auto_TODO.xml"));
            }
        }

        private static XDocument BuildLanguageDataDocument(
            Dictionary<string, InjectionValue> entries,
            bool todoMode)
        {
            var languageData = new XElement("LanguageData");

            foreach (InjectionValue entry in entries.Values.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (entry.IsCollection)
                {
                    var element = new XElement(entry.Key);
                    foreach (string value in entry.Values)
                    {
                        element.Add(new XElement("li", todoMode ? ToTodoListItem(value) : (value ?? string.Empty)));
                    }
                    languageData.Add(element);
                }
                else
                {
                    string value = entry.Values.FirstOrDefault() ?? string.Empty;
                    languageData.Add(new XElement(entry.Key, todoMode ? "TODO" : value));
                }
            }

            return new XDocument(new XDeclaration("1.0", "utf-8", null), languageData);
        }

        private static void WriteWorklistTsv(
            Dictionary<string, Dictionary<string, InjectionValue>> entriesByFolder,
            string outTsvPath)
        {
            var lines = new List<string> { "Folder\tKey\tCNSourceType\tCNSource" };

            foreach (KeyValuePair<string, Dictionary<string, InjectionValue>> folderPair in entriesByFolder)
            {
                string folder = folderPair.Key;
                foreach (InjectionValue entry in folderPair.Value.Values.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (entry.IsCollection)
                    {
                        string joined = string.Join("\\n", entry.Values.Select(v => v ?? string.Empty));
                        lines.Add($"{EscapeTsv(folder)}\t{EscapeTsv(entry.Key)}\tlist\t{EscapeTsv(joined)}");
                    }
                    else
                    {
                        string value = entry.Values.FirstOrDefault() ?? string.Empty;
                        lines.Add($"{EscapeTsv(folder)}\t{EscapeTsv(entry.Key)}\ttext\t{EscapeTsv(value)}");
                    }
                }
            }

            string dir = Path.GetDirectoryName(outTsvPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllLines(outTsvPath, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        private static string GetDefInjectedFolderName(Type defType)
        {
            if (defType == null)
            {
                return "UnknownDefType";
            }

            string ns = defType.Namespace ?? string.Empty;
            if (ns == "Verse" || ns == "RimWorld")
            {
                return defType.Name;
            }

            return defType.FullName ?? defType.Name;
        }

        private static string SanitizePathSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                return "_";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                segment = segment.Replace(c, '_');
            }

            return segment;
        }

        private static string ToTodoListItem(string original)
        {
            string s = original ?? string.Empty;
            int idx = s.IndexOf("->", StringComparison.Ordinal);
            if (idx >= 0)
            {
                return s.Substring(0, idx) + "->TODO";
            }

            return "TODO";
        }

        private static bool KeepValue(string key, FieldInfo fieldInfo, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(key))
            {
                if (key.IndexOf(".defName", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                if (key.IndexOf(".fileName", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }

            if (LooksLikeFilePath(value))
            {
                return false;
            }

            if (LooksLikeAssetPath(value))
            {
                return false;
            }

            string fieldName = fieldInfo?.Name ?? string.Empty;
            if (fieldName.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (fieldName.IndexOf("description", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (fieldName.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (fieldName.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return true;
                }
            }

            if (value.IndexOfAny(new[] { ' ', '\n', '\r', '\t' }) >= 0)
            {
                return true;
            }

            if (value.IndexOfAny(new[] { '，', '。', '？', '！', '：', '；', '、', '（', '）', '《', '》', '“', '”', '"', '\'', ':', ';', ',', '.', '!', '?' }) >= 0)
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeFilePath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && (value[2] == '\\' || value[2] == '/'))
            {
                return true;
            }

            if (value.Contains("\\\\") || value.Contains(":\\"))
            {
                return true;
            }

            return false;
        }

        private static bool LooksLikeAssetPath(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (value.IndexOf('/') < 0 && value.IndexOf('\\') < 0)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    return false;
                }
            }

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c))
                {
                    return false;
                }
            }

            return true;
        }

        private static void WriteXmlUtf8Indented(XDocument doc, string path)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = false,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };

            using XmlWriter writer = XmlWriter.Create(path, settings);
            doc.Save(writer);
        }

        private static string EscapeTsv(string value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value.IndexOfAny(new[] { '\t', '\n', '\r', '"' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
