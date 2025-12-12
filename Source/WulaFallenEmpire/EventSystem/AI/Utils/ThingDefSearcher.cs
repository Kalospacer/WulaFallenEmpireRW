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

        /// <summary>
        /// Parses a natural language request string into a list of spawnable items.
        /// Example: "5 beef, 1 persona core, 30 wood"
        /// </summary>
        public static List<SearchResult> ParseAndSearch(string request)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrEmpty(request)) return results;

            // Split by common separators
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

        private static SearchResult ParseSingleItem(string itemRequest)
        {
            // Extract count and name
            // Regex to match "number name" or "name number" or "number个name"
            // Supports Chinese and English numbers
            
            int count = 1;
            string nameQuery = itemRequest;

            // Try to find digits
            var match = Regex.Match(itemRequest, @"(\d+)");
            if (match.Success)
            {
                if (int.TryParse(match.Value, out int parsedCount))
                {
                    count = parsedCount;
                    // Remove the number from the query string to get the name
                    nameQuery = itemRequest.Replace(match.Value, "").Trim();
                    // Remove common quantifiers
                    nameQuery = Regex.Replace(nameQuery, @"[个只把张条xX×]", "").Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(nameQuery))
            {
                return new SearchResult { Def = null, Count = 0, Score = 0 };
            }

            // Search for the Def
            var bestMatch = FindBestThingDef(nameQuery);
            
            return new SearchResult
            {
                Def = bestMatch.Def,
                Count = count,
                Score = bestMatch.Score
            };
        }

        private static (ThingDef Def, float Score) FindBestThingDef(string query)
        {
            ThingDef bestDef = null;
            float bestScore = 0f;
            string lowerQuery = query.ToLower();

            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                // Filter out non-items or abstract defs
                if (def.category != ThingCategory.Item && def.category != ThingCategory.Building) continue;
                if (def.label == null) continue;

                float score = 0f;
                string label = def.label.ToLower();
                string defName = def.defName.ToLower();

                // Exact match
                if (label == lowerQuery) score = 1.0f;
                else if (defName == lowerQuery) score = 0.9f;
                // Contains match
                else if (label.Contains(lowerQuery))
                {
                    // Shorter labels that contain the query are better matches
                    score = 0.6f + (0.2f * ((float)lowerQuery.Length / label.Length));
                }
                else if (defName.Contains(lowerQuery))
                {
                    score = 0.5f;
                }
                
                // Bonus for tradeability (more likely to be what player wants)
                if (def.tradeability != Tradeability.None) score += 0.05f;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDef = def;
                }
            }

            // Threshold
            if (bestScore < 0.4f) return (null, 0f);

            return (bestDef, bestScore);
        }
    }
}