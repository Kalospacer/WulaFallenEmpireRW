using System;
using System.Collections.Generic;
using Verse;

namespace WulaFallenEmpire.EventSystem.AI.Tools
{
    public abstract class AITool
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public abstract string UsageSchema { get; } // JSON schema or simple description of args

        public abstract string Execute(string args);
    }
}