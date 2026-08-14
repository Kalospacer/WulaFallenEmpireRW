using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RimWorld;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Utils
{
    public static class PawnKindDefSearcher
    {
        public struct SearchResult
        {
            public PawnKindDef Def;
            public float Score;
        }

        public static List<SearchResult> Search(string query, int maxResults = 20, float minScore = 0.15f)
        {
            var results = new List<SearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            query = query.Trim();
            string lowerQuery = query.ToLowerInvariant();
            string normalizedQuery = NormalizeKey(lowerQuery);
            var tokens = TokenizeQuery(lowerQuery);

            foreach (var def in DefDatabase<PawnKindDef>.AllDefs)
            {
                if (def == null) continue;

                float score = ScorePawnKindDef(def, lowerQuery, normalizedQuery, tokens);
                if (score >= minScore)
                {
                    results.Add(new SearchResult { Def = def, Score = score });
                }
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            if (results.Count > maxResults) results.RemoveRange(maxResults, results.Count - maxResults);
            return results;
        }

        private static float ScorePawnKindDef(PawnKindDef def, string lowerQuery, string normalizedQuery, List<string> tokens)
        {
            float score = 0f;

            foreach (var candidate in GetCandidateStrings(def))
            {
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                score = Math.Max(score, ScoreText(candidate, lowerQuery, normalizedQuery, tokens));
            }

            if (score > 1.0f) score = 1.0f;
            return score;
        }

        private static IEnumerable<string> GetCandidateStrings(PawnKindDef def)
        {
            yield return def.label;
            yield return def.labelPlural;
            yield return def.defName;
            if (def.race != null)
            {
                yield return def.race.label;
                yield return def.race.defName;
            }
        }

        private static float ScoreText(string candidate, string lowerQuery, string normalizedQuery, List<string> tokens)
        {
            string lowerCandidate = candidate.ToLowerInvariant();
            string normalizedCandidate = NormalizeKey(lowerCandidate);
            float score = 0f;

            if (!string.IsNullOrEmpty(normalizedQuery))
            {
                if (normalizedCandidate == normalizedQuery) score = Math.Max(score, 1.00f);
            }

            if (!string.IsNullOrEmpty(lowerQuery))
            {
                if (lowerCandidate == lowerQuery) score = Math.Max(score, 0.95f);
                if (lowerCandidate.StartsWith(lowerQuery)) score = Math.Max(score, 0.80f);
                if (lowerCandidate.Contains(lowerQuery))
                {
                    score = Math.Max(score, 0.65f + 0.15f * ((float)lowerQuery.Length / Math.Max(1, lowerCandidate.Length)));
                }
            }

            if (tokens.Count > 0)
            {
                int matched = tokens.Count(t => normalizedCandidate.Contains(NormalizeKey(t)));
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

            if (!string.IsNullOrEmpty(normalizedQuery) && normalizedQuery.Length >= 2 && IsCjkString(normalizedQuery))
            {
                if (IsCjkString(normalizedCandidate) && IsCjkSubsequence(normalizedQuery, normalizedCandidate))
                {
                    float coverage = (float)normalizedQuery.Length / Math.Max(1, normalizedCandidate.Length);
                    score = Math.Max(score, 0.50f + 0.30f * coverage);
                }
            }

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
                AddCjkBigrams(cleaned, tokens);
            }

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
            return (c >= '\u4E00' && c <= '\u9FFF') ||
                   (c >= '\u3400' && c <= '\u4DBF') ||
                   (c >= '\uF900' && c <= '\uFAFF');
        }

        private static bool IsCjkString(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (!IsCjkChar(s[i])) return false;
            }
            return true;
        }

        private static bool IsCjkSubsequence(string query, string target)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(target)) return false;
            int qi = 0;
            for (int ti = 0; ti < target.Length && qi < query.Length; ti++)
            {
                if (target[ti] == query[qi]) qi++;
            }
            return qi == query.Length;
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
