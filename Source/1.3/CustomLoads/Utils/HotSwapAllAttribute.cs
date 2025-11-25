using System;

namespace CustomLoads.Utils
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class HotSwapAllAttribute : Attribute { }
}
