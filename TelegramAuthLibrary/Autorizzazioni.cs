using System.Collections.Generic;
using System.Xml.Serialization;

namespace TelegramAuthLibrary
{
    [XmlRootAttribute(Namespace = "", IsNullable = false)]
    public class Autorizzazioni
    {
        [XmlElement("Device")]
        public List<Device> Items { get; set; }
    }
}
