using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Represents a single memory entry extracted from conversations.
    /// Inspired by Mem0's memory structure.
    /// </summary>
    public class AIMemoryEntry
    {
        /// <summary>Unique identifier for this memory</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>The actual memory content/fact</summary>
        [JsonProperty("fact")]
        public string Fact { get; set; }

        /// <summary>
        /// Category of memory: preference, personal, plan, colony, misc
        /// </summary>
        [JsonProperty("category")]
        public string Category { get; set; }

        /// <summary>Game ticks when this memory was created</summary>
        [JsonProperty("createdTicks")]
        public long CreatedTicks { get; set; }

        /// <summary>Game ticks when this memory was last updated</summary>
        [JsonProperty("updatedTicks")]
        public long UpdatedTicks { get; set; }

        /// <summary>Number of times this memory has been accessed/retrieved</summary>
        [JsonProperty("accessCount")]
        public int AccessCount { get; set; }

        /// <summary>Hash of the fact for quick duplicate detection</summary>
        [JsonProperty("hash")]
        public string Hash { get; set; }

        public AIMemoryEntry()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 12);
            CreatedTicks = 0;
            UpdatedTicks = 0;
            AccessCount = 0;
            Category = "misc";
        }

        public AIMemoryEntry(string fact, string category = "misc") : this()
        {
            Fact = fact;
            Category = category ?? "misc";
            Hash = ComputeHash(fact);
        }

        public static string ComputeHash(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string normalized = NormalizeForHash(text);
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                StringBuilder sb = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static string NormalizeForHash(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            return string.Join(" ", text.Trim().ToLowerInvariant().Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
        }

        public void UpdateFact(string newFact)
        {
            Fact = newFact;
            Hash = ComputeHash(newFact);
        }

        public void MarkAccessed()
        {
            AccessCount++;
        }
    }
}
