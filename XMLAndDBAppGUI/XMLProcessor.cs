using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Xml;

public class XmlProcessorResult
{
    public bool Success { get; set; }
    public long ZglvId { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class XmlProcessor
{
    private readonly string _databasePath;
    private readonly string _xsdPath;

    public XmlProcessor(string databasePath, string xsdPath)
    {
        _databasePath = databasePath;
        _xsdPath = xsdPath;
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public XmlProcessorResult ProcessFile(string xmlPath, ZlList data)
    {
        var result = new XmlProcessorResult();

        if (!ValidateXml(xmlPath, out var xsdErrors))
        {
            result.Errors = xsdErrors;
            return result;
        }

        try
        {
            result.ZglvId = SaveToDatabase(data);
            result.Success = true;
        }
        catch (DbUpdateException exception)
        {
            result.Errors.Add($"Откат транзакции СУБД: {exception.Message}");
        }
        catch (Exception exception)
        {
            result.Errors.Add($"Критическая ошибка сохранения в БД: {exception.Message}");
        }

        return result;
    }

    public bool ValidateXml(string xmlPath, out List<string> validationErrors)
    {
        var errors = new List<string>();
        validationErrors = errors;
        var settings = new XmlReaderSettings();
        settings.Schemas.Add(null, _xsdPath);
        settings.ValidationType = ValidationType.Schema;
        settings.ValidationEventHandler += (_, args) =>
            errors.Add($"[Строка {args.Exception.LineNumber}, Позиция {args.Exception.LinePosition}]: {args.Message}");

        using var reader = XmlReader.Create(xmlPath, settings);
        while (reader.Read()) { }

        return validationErrors.Count == 0;
    }

    public long SaveToDatabase(ZlList data)
    {
        using var database = RegistryDbContextFactory.Create(_databasePath);
        using var transaction = database.Database.BeginTransaction();

        var existingRegistry = database.Registries
            .SingleOrDefault(item => item.FileName == data.Header.FileName);
        if (existingRegistry is not null)
        {
            database.Registries.Remove(existingRegistry);
            database.SaveChanges();
        }

        var registry = MapRegistry(data);
        database.Registries.Add(registry);
        database.SaveChanges();

        var checkErrors = VerifyGenderCountsOnDbLevel(database, registry.Id);
        if (checkErrors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("; ", checkErrors));
        }

        transaction.Commit();
        return registry.Id;
    }

    public List<string> VerifyGenderCountsOnDbLevel(long zglvId)
    {
        using var database = RegistryDbContextFactory.Create(_databasePath);
        return VerifyGenderCountsOnDbLevel(database, zglvId);
    }

    private static Registry MapRegistry(ZlList data)
    {
        var registry = new Registry
        {
            Version = data.Header.Version,
            Data = data.Header.Data,
            FileName = data.Header.FileName,
            Year = data.Header.Year,
            CodeMo = data.Header.CodeMo
        };

        foreach (var sourceEvent in data.Events)
        {
            var registryEvent = new RegistryEvent
            {
                Disp = sourceEvent.Disp,
                KolM = sourceEvent.KolM,
                KolW = sourceEvent.KolW
            };

            foreach (var sourcePerson in sourceEvent.Persons ?? [])
            {
                registryEvent.Persons.Add(MapPerson(sourcePerson));
            }

            registry.Events.Add(registryEvent);
        }

        return registry;
    }

    private static RegistryPerson MapPerson(PersItem source)
    {
        return new RegistryPerson
        {
            NZap = source.NZap,
            IdPac = source.IdPac,
            Gender = source.Gender,
            Dr = source.Dr,
            Smo = source.Smo,
            VPolis = source.VPolis,
            SPolis = source.SPolis,
            NPolis = source.NPolis,
            Quarter = source.Quarter,
            Month = source.Month,
            Lpu1 = source.Lpu1,
            Depth = source.Depth,
            SsDoc = source.SsDoc,
            SsDocD = source.SsDocD,
            PrvsD = source.PrvsD,
            DsD = source.DsD,
            PlaceD = source.PlaceD,
            IdTfoms = source.IdTfoms,
            Comment = source.Comment,
            Contacts = source.Contacts is null ? null : new PersonContacts
            {
                PhoneF = JoinValues(source.Contacts.PhoneF),
                PhoneM = JoinValues(source.Contacts.PhoneM),
                Email = JoinValues(source.Contacts.Email),
                Address = source.Contacts.Address
            }
        };
    }

    private static string? JoinValues(List<string>? values) => values is { Count: > 0 } ? string.Join(";", values) : null;

    private static List<string> VerifyGenderCountsOnDbLevel(RegistryDbContext database, long zglvId)
    {
        var events = database.Events
            .Where(item => item.RegistryId == zglvId)
            .Select(item => new
            {
                item.Id,
                item.Disp,
                ExpectedMen = item.KolM,
                ExpectedWomen = item.KolW,
                ActualMen = item.Persons.Count(person => person.Gender == 1),
                ActualWomen = item.Persons.Count(person => person.Gender == 2)
            })
            .ToList();

        var errors = new List<string>();
        foreach (var item in events)
        {
            Console.WriteLine($"[Статистика EVENT ID={item.Id}, DISP={item.Disp}]:");
            Console.WriteLine($"├─ Мужчины (KOL_M): ожидалось = {item.ExpectedMen}, фактически в PERS = {item.ActualMen}");
            Console.WriteLine($"└─ Женщины (KOL_W): ожидалось = {item.ExpectedWomen}, фактически в PERS = {item.ActualWomen}");

            if (item.ExpectedMen != item.ActualMen)
            {
                errors.Add($"[EVENT ID={item.Id}, DISP={item.Disp}]: Несоответствие мужчин. Ожидалось KOL_M={item.ExpectedMen}, фактически в PERS={item.ActualMen}");
            }

            if (item.ExpectedWomen != item.ActualWomen)
            {
                errors.Add($"[EVENT ID={item.Id}, DISP={item.Disp}]: Несоответствие женщин. Ожидалось KOL_W={item.ExpectedWomen}, фактически в PERS={item.ActualWomen}");
            }
        }

        return errors;
    }
}
