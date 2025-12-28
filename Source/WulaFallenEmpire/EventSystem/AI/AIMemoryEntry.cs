using System;

namespace WulaFallenEmpire.EventSystem.AI
{
    /// <summary>
    /// Represents a single memory entry extracted from conversations.
    /// Inspired by Mem0's memory structure.
    /// </summary>
    public class AIMemoryEntry
    {
        /// <summary>Unique identifier for this memory</summary>
        public string Id { get; set; }

        /// <summary>The actual memory content/fact</summary>
        public string Fact { get; set; }

        /// <summary>
        /// Category of memory: preference, personal, plan, colony, misc
        /// </summary>
        public string Category { get; set; }

        /// <summary>Game ticks when this memory was created</summary>
        public long CreatedTicks { get; set; }

        /// <summary>Game ticks when this memory was last updated</summary>
        public long UpdatedTicks { get; set; }

        /// <summary>Number of times this memory has been accessed/retrieved</summary>
        public int AccessCount { get; set; }

        /// <summary>Hash of the fact for quick duplicate detection</summary>
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
            // Simple hash based on normalized text
            string normalized = text.ToLowerInvariant().Trim();
            return normalized.GetHashCode().ToString("X8");
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
