using Microsoft.EntityFrameworkCore;

public sealed class RegistryDbContext : DbContext
{
    public RegistryDbContext(DbContextOptions<RegistryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Registry> Registries => Set<Registry>();
    public DbSet<RegistryEvent> Events => Set<RegistryEvent>();
    public DbSet<RegistryPerson> Persons => Set<RegistryPerson>();
    public DbSet<PersonContacts> Contacts => Set<PersonContacts>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Registry>(entity =>
        {
            entity.ToTable("ZGLV");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("ID");
            entity.Property(item => item.Version).HasColumnName("VERSION").IsRequired();
            entity.Property(item => item.Data).HasColumnName("DATA").IsRequired();
            entity.Property(item => item.FileName).HasColumnName("FILENAME").IsRequired();
            entity.Property(item => item.Year).HasColumnName("YEAR");
            entity.Property(item => item.CodeMo).HasColumnName("CODE_MO").IsRequired();
            entity.HasIndex(item => item.FileName).IsUnique();
            entity.HasMany(item => item.Events).WithOne(item => item.Registry).HasForeignKey(item => item.RegistryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegistryEvent>(entity =>
        {
            entity.ToTable("EVENT");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("ID");
            entity.Property(item => item.RegistryId).HasColumnName("ZGLV_ID");
            entity.Property(item => item.Disp).HasColumnName("DISP").IsRequired();
            entity.Property(item => item.KolM).HasColumnName("KOL_M");
            entity.Property(item => item.KolW).HasColumnName("KOL_W");
            entity.HasMany(item => item.Persons).WithOne(item => item.Event).HasForeignKey(item => item.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RegistryPerson>(entity =>
        {
            entity.ToTable("PERS");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("ID");
            entity.Property(item => item.EventId).HasColumnName("EVENT_ID");
            entity.Property(item => item.NZap).HasColumnName("N_ZAP");
            entity.Property(item => item.IdPac).HasColumnName("ID_PAC").IsRequired();
            entity.Property(item => item.Gender).HasColumnName("W");
            entity.Property(item => item.Dr).HasColumnName("DR").IsRequired();
            entity.Property(item => item.Smo).HasColumnName("SMO").IsRequired();
            entity.Property(item => item.VPolis).HasColumnName("VPOLIS");
            entity.Property(item => item.SPolis).HasColumnName("SPOLIS");
            entity.Property(item => item.NPolis).HasColumnName("NPOLIS").IsRequired();
            entity.Property(item => item.Quarter).HasColumnName("QUARTER");
            entity.Property(item => item.Month).HasColumnName("MONTH");
            entity.Property(item => item.Lpu1).HasColumnName("LPU1").IsRequired();
            entity.Property(item => item.Depth).HasColumnName("DEPTH").IsRequired();
            entity.Property(item => item.SsDoc).HasColumnName("SS_DOC").IsRequired();
            entity.Property(item => item.SsDocD).HasColumnName("SS_DOC_D");
            entity.Property(item => item.PrvsD).HasColumnName("PRVS_D");
            entity.Property(item => item.DsD).HasColumnName("DS_D");
            entity.Property(item => item.PlaceD).HasColumnName("PLACE_D");
            entity.Property(item => item.IdTfoms).HasColumnName("ID_TFOMS");
            entity.Property(item => item.Comment).HasColumnName("COMMENT");
            entity.HasOne(item => item.Contacts).WithOne(item => item.Person).HasForeignKey<PersonContacts>(item => item.PersonId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PersonContacts>(entity =>
        {
            entity.ToTable("CONTACTS");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).HasColumnName("ID");
            entity.Property(item => item.PersonId).HasColumnName("PERS_ID");
            entity.Property(item => item.PhoneF).HasColumnName("PHONE_F");
            entity.Property(item => item.PhoneM).HasColumnName("PHONE_M");
            entity.Property(item => item.Email).HasColumnName("EMAIL");
            entity.Property(item => item.Address).HasColumnName("ADDRESS");
        });
    }
}

public static class RegistryDbContextFactory
{
    // Смена СУБД ограничена этой фабрикой: замените провайдер и его конфигурацию,
    // а прикладные LINQ-запросы и модели останутся без изменений.
    public static RegistryDbContext Create(string databasePath)
    {
        var options = new DbContextOptionsBuilder<RegistryDbContext>()
            .UseSqlite($"Data Source={databasePath};Foreign Keys=True")
            .Options;

        return new RegistryDbContext(options);
    }
}

public sealed class Registry
{
    public long Id { get; set; }
    public string Version { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int Year { get; set; }
    public string CodeMo { get; set; } = string.Empty;
    public List<RegistryEvent> Events { get; set; } = new();
}

public sealed class RegistryEvent
{
    public long Id { get; set; }
    public long RegistryId { get; set; }
    public string Disp { get; set; } = string.Empty;
    public int KolM { get; set; }
    public int KolW { get; set; }
    public Registry Registry { get; set; } = null!;
    public List<RegistryPerson> Persons { get; set; } = new();
}

public sealed class RegistryPerson
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public int NZap { get; set; }
    public string IdPac { get; set; } = string.Empty;
    public int Gender { get; set; }
    public string Dr { get; set; } = string.Empty;
    public string Smo { get; set; } = string.Empty;
    public int VPolis { get; set; }
    public string? SPolis { get; set; }
    public string NPolis { get; set; } = string.Empty;
    public int? Quarter { get; set; }
    public int? Month { get; set; }
    public string Lpu1 { get; set; } = string.Empty;
    public string Depth { get; set; } = string.Empty;
    public string SsDoc { get; set; } = string.Empty;
    public string? SsDocD { get; set; }
    public int? PrvsD { get; set; }
    public string? DsD { get; set; }
    public int? PlaceD { get; set; }
    public string? IdTfoms { get; set; }
    public string? Comment { get; set; }
    public RegistryEvent Event { get; set; } = null!;
    public PersonContacts? Contacts { get; set; }
}

public sealed class PersonContacts
{
    public long Id { get; set; }
    public long PersonId { get; set; }
    public string? PhoneF { get; set; }
    public string? PhoneM { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public RegistryPerson Person { get; set; } = null!;
}

public sealed class EventGridRow
{
    public long Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string Disp { get; init; } = string.Empty;
    public int DeclaredMen { get; init; }
    public int DeclaredWomen { get; init; }
    public int ActualMen { get; init; }
    public int ActualWomen { get; init; }
}

public sealed class PersonGridRow
{
    public long Id { get; init; }
    public long EventId { get; init; }
    public int NZap { get; init; }
    public string IdPac { get; init; } = string.Empty;
    public string Gender { get; init; } = string.Empty;
    public string Dr { get; init; } = string.Empty;
    public string NPolis { get; init; } = string.Empty;
    public string Lpu1 { get; init; } = string.Empty;
    public string? DsD { get; init; }
}
