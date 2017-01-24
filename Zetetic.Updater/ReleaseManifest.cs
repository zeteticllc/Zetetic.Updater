using System.Xml.Serialization;

namespace Zetetic.Updater
{
    [XmlRoot("ReleaseManifest")]
    public class ReleaseManifest
    {
        [XmlElement("Name")]
        public string Name { get; set; }
        
        [XmlElement("Version")]
        public string Version { get; set; }

        [XmlElement("PackageUrl")]
        public string PackageUrl { get; set; }

        [XmlElement("ReleaseNotesUrl")]
        public string ReleaseNotesUrl { get; set; }
    }
}