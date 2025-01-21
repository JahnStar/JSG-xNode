using System;

namespace XNode {
    /// <summary>
    ///
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class NodeFieldAttribute : Attribute
    {
        public readonly string label;
        public readonly bool hideInNode;
        public NodeFieldAttribute(string label = "", bool hideInNode = false)
        {
            this.label = label;
            this.hideInNode = hideInNode;
        }
    }
}