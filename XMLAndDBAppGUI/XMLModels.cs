using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[XmlRoot("ZL_LIST")]
public class ZlList
{
    [XmlElement("ZGLV")]
    public Zglv Header { get; set; }

    [XmlElement("EVENT")]
    public List<EventItem> Events { get; set; } = new();
}

public class Zglv
{
    [XmlElement("VERSION")]
    public string Version { get; set; }

    [XmlElement("DATA")]
    public string Data { get; set; }

    [XmlElement("FILENAME")]
    public string FileName { get; set; }

    [XmlElement("YEAR")]
    public int Year { get; set; }

    [XmlElement("CODE_MO")]
    public string CodeMo { get; set; }
}

public class EventItem
{
    [XmlElement("DISP")]
    public string Disp { get; set; }

    [XmlElement("KOL_M")]
    public int KolM { get; set; }

    [XmlElement("KOL_W")]
    public int KolW { get; set; }

    [XmlElement("PERS")]
    public List<PersItem> Persons { get; set; } = new();
}

public class PersItem
{
    [XmlElement("N_ZAP")]
    public int NZap { get; set; }

    [XmlElement("ID_PAC")]
    public string IdPac { get; set; }

    [XmlElement("W")]
    public int Gender { get; set; } // 1 - М, 2 - Ж (согласно классификатору V005)

    [XmlElement("DR")]
    public string Dr { get; set; }

    [XmlElement("SMO")]
    public string Smo { get; set; }

    [XmlElement("VPOLIS")]
    public int VPolis { get; set; }

    [XmlElement("SPOLIS")]
    public string SPolis { get; set; }

    [XmlElement("NPOLIS")]
    public string NPolis { get; set; }

    [XmlElement("CONTACTS")]
    public ContactsItem Contacts { get; set; }

    [XmlElement("QUARTER")]
    public int? Quarter { get; set; }

    [XmlElement("MONTH")]
    public int? Month { get; set; }

    [XmlElement("LPU1")]
    public string Lpu1 { get; set; }

    [XmlElement("DEPTH")]
    public string Depth { get; set; }

    [XmlElement("SS_DOC")]
    public string SsDoc { get; set; }

    [XmlElement("SS_DOC_D")]
    public string SsDocD { get; set; }

    [XmlElement("PRVS_D")]
    public int? PrvsD { get; set; }

    [XmlElement("DS_D")]
    public string DsD { get; set; }

    [XmlElement("PLACE_D")]
    public int? PlaceD { get; set; }

    [XmlElement("ID_TFOMS")]
    public string IdTfoms { get; set; }

    [XmlElement("COMMENT")]
    public string Comment { get; set; }
}

public class ContactsItem
{
    [XmlElement("PHONE_F")]
    public List<string> PhoneF { get; set; } = new();

    [XmlElement("PHONE_M")]
    public List<string> PhoneM { get; set; } = new();

    [XmlElement("EMAIL")]
    public List<string> Email { get; set; } = new();

    [XmlElement("ADDRESS")]
    public string Address { get; set; }
}