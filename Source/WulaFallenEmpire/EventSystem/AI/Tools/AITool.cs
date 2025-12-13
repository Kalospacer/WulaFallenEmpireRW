using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public abstract class AITool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string UsageSchema { get; } // XML schema description

        public abstract string Execute(string args);

        /// <summary>
        /// Helper method to parse XML arguments into a dictionary.
        /// Supports simple tags and CDATA blocks.
        /// </summary>
        protected Dictionary<string, string> ParseXmlArgs(string xml)
        {
            var argsDict = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(xml)) return argsDict;

            // Regex to match <tag>value</tag> or <tag><![CDATA[value]]></tag>
            // Group 1: Tag name
            // Group 2: CDATA value
            // Group 3: Simple value
            var paramMatches = Regex.Matches(xml, @"<([a-zA-Z0-9_]+)>(?:<!\[CDATA\[(.*?)]]>|(.*?))</\1>", RegexOptions.Singleline);

            foreach (Match match in paramMatches)
            {
                string key = match.Groups[1].Value;
                string value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                argsDict[key] = value;
            }

            return argsDict;
        }
    }
}