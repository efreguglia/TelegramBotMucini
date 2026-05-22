using System.Xml.Serialization;

namespace TelegramAuthLibrary
{
    [XmlTypeAttribute(AnonymousType = true)]
    public class Device
    {
        [XmlAttribute(attributeName: "id")]
        public string Id { get; set; }
        [XmlElement]
        public bool Agenda { get; set; }
        [XmlElement]
        public bool Registrato { get; set; }
        [XmlElement]
        public bool UtenteAttivo { get; set; }
    }
}
