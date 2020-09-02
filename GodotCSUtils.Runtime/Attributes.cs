using System;

namespace GodotCSUtils
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class GetAttribute : Attribute
    {
        public GetAttribute(string path = null)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class AutoloadAttribute : Attribute
    {
        public AutoloadAttribute(string name = null)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class ExportRenameAttribute : Attribute
    {
        public ExportRenameAttribute(string name = null)
        {
        }
    }
}