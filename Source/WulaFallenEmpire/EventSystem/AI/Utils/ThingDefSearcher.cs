using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    public static class ThingDefSearcher
    {
        public struct SearchResult
        {
            public ThingDef Def;
            public int Count;
            public float Score;
        }

        public static List<SearchResult> Search(string query, int maxResults = 20, bool itemsOnly = false, float minScore = 0.15f)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            query = query.Trim();
            string lowerQuery = query.ToLowerInvariant();

            string normalizedQuery = NormalizeKey(lowerQuery);
            var tokens = TokenizeQuery(lowerQuery);

            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def == null || def.label == null) continue;
                if (def.category != ThingCategory.Item && def.category != ThingCategory.Building) continue;
                if (itemsOnly && def.category != ThingCategory.Item) continue;

                float score = ScoreThingDef(def, lowerQuery, normalizedQuery, tokens);
                if (score >= minScore)
                {
                    results.Add(new SearchResult { Def = def, Count = 1, Score = score });
                }
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
            return results;
        }

        /// <summary>
        /// Parses a natural language request string into a list of spawnable items.
        /// Example: "5 beef, 1 persona core, 30 wood"
        /// </summary>
        public static List<SearchResult> ParseAndSearch(string request)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrEmpty(request)) return results;

            var parts = request.Split(new[] { ',', '，', ';', '、', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var result = ParseSingleItem(part.Trim());
                if (result.Def != null)
                {
                    results.Add(result);
                }
            }

            return results;
        }

        public static bool TryFindBestThingDef(string query, out ThingDef bestDef, out float bestScore, bool itemsOnly = false, float minScore = 0.4f)
        {
            bestDef = null;
            bestScore = 0f;
            var results = Search(query, maxResults: 1, itemsOnly: itemsOnly, minScore: minScore);
            if (results.Count == 0) return false;
            bestDef = results[0].Def;
            bestScore = results[0].Score;
            return true;
        }

        private static SearchResult ParseSingleItem(string itemRequest)
        {
            int count = 1;
            string nameQuery = itemRequest;

            var match = Regex.Match(itemRequest, @"(\d+)");
            if (match.Success && int.TryParse(match.Value, out int parsedCount))
            {
                count = parsedCount;
                nameQuery = itemRequest.Replace(match.Value, "").Trim();
                nameQuery = Regex.Replace(nameQuery, @"[个只把张条xX×]", "").Trim();
            }

            if (string.IsNullOrWhiteSpace(nameQuery))
            {
                return new SearchResult { Def = null, Count = 0, Score = 0 };
            }

            TryFindBestThingDef(nameQuery, out ThingDef bestDef, out float bestScore, itemsOnly: false, minScore: 0.15f);
            return new SearchResult
            {
                Def = bestDef,
                Count = count,
                Score = bestScore
            };
        }

        private static float ScoreThingDef(ThingDef def, string lowerQuery, string normalizedQuery, List<string> tokens)
        {
            string label = def.label?.ToLowerInvariant() ?? "";
            string defName = def.defName?.ToLowerInvariant() ?? "";
            string normalizedLabel = NormalizeKey(label);
            string normalizedDefName = NormalizeKey(defName);

            float score = 0f;

            if (!string.IsNullOrEmpty(normalizedQuery))
            {
                if (normalizedLabel == normalizedQuery) score = Math.Max(score, 1.00f);
                if (normalizedDefName == normalizedQuery) score = Math.Max(score, 0.98f);
            }

            if (!string.IsNullOrEmpty(lowerQuery))
            {
                if (label == lowerQuery) score = Math.Max(score, 0.95f);
                if (defName == lowerQuery) score = Math.Max(score, 0.93f);

                if (label.StartsWith(lowerQuery)) score = Math.Max(score, 0.80f);
                if (defName.StartsWith(lowerQuery)) score = Math.Max(score, 0.85f);

                if (label.Contains(lowerQuery)) score = Math.Max(score, 0.65f + 0.15f * ((float)lowerQuery.Length / Math.Max(1, label.Length)));
                if (defName.Contains(lowerQuery)) score = Math.Max(score, 0.60f + 0.15f * ((float)lowerQuery.Length / Math.Max(1, defName.Length)));
            }

            if (tokens.Count > 0)
            {
                int matchedInLabel = tokens.Count(t => normalizedLabel.Contains(NormalizeKey(t)));
                int matchedInDefName = tokens.Count(t => normalizedDefName.Contains(NormalizeKey(t)));
                int matched = Math.Max(matchedInLabel, matchedInDefName);
                float coverage = (float)matched / tokens.Count;

                if (matched > 0)
                {
                    score = Math.Max(score, 0.45f + 0.35f * coverage);
                }

                if (matched == tokens.Count && tokens.Count >= 2)
                {
                    score = Math.Max(score, 0.80f);
                }
            }

            bool queryLooksLikeFood = tokens.Any(t => t == "meal" || t == "food" || t.Contains("meal") || t.Contains("food")) ||
                                      lowerQuery.Contains("食") || lowerQuery.Contains("饭") || lowerQuery.Contains("餐");
            if (queryLooksLikeFood && def.ingestible != null)
            {
                score += 0.05f;
            }

            if (def.tradeability != Tradeability.None) score += 0.03f;
            if (!def.IsStuff) score += 0.01f;

            if (score > 1.0f) score = 1.0f;
            return score;
        }

        private static List<string> TokenizeQuery(string lowerQuery)
        {
            if (string.IsNullOrWhiteSpace(lowerQuery)) return new List<string>();

            string q = lowerQuery.Trim();
            q = q.Replace('_', ' ').Replace('-', ' ');
            var rawTokens = Regex.Split(q, @"\s+").Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

            var tokens = new List<string>();
            foreach (var token in rawTokens)
            {
                string cleaned = Regex.Replace(token, @"[^\p{L}\p{N}]+", "");
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                tokens.Add(cleaned);

                // CJK queries often have no spaces; add bigrams for better partial matching
                // (e.g. "乌拉能源核心" should match "乌拉帝国能源核心").
                AddCjkBigrams(cleaned, tokens);
            }

            // For queries like "fine meal" also consider the normalized concatenation for matching "MealFine".
            if (tokens.Count >= 2)
            {
                tokens.Add(string.Concat(tokens));
            }

            return tokens.Distinct().ToList();
        }

        private static void AddCjkBigrams(string token, List<string> tokens)
        {
            if (string.IsNullOrEmpty(token) || token.Length < 2) return;

            int runStart = -1;
            for (int i = 0; i < token.Length; i++)
            {
                bool isCjk = IsCjkChar(token[i]);
                if (isCjk)
                {
                    if (runStart == -1) runStart = i;
                }
                else
                {
                    if (runStart != -1)
                    {
                        AddBigramsForRun(token, runStart, i - 1, tokens);
                        runStart = -1;
                    }
                }
            }

            if (runStart != -1)
            {
                AddBigramsForRun(token, runStart, token.Length - 1, tokens);
            }
        }

        private static void AddBigramsForRun(string token, int start, int end, List<string> tokens)
        {
            int len = end - start + 1;
            if (len < 2) return;

            // cap to avoid generating too many tokens for long Chinese sentences
            int maxBigrams = 32;
            int added = 0;

            for (int i = start; i < end; i++)
            {
                tokens.Add(token.Substring(i, 2));
                added++;
                if (added >= maxBigrams) break;
            }
        }

        private static bool IsCjkChar(char c)
        {
            // Basic CJK ranges commonly used in Chinese/Japanese/Korean text.
            return (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        private static string NormalizeKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string t = s.ToLowerInvariant();
            t = Regex.Replace(t, @"[\s_\-]+", "");
            t = Regex.Replace(t, @"[^\p{L}\p{N}]+", "");
            return t;
        }
    }
}
