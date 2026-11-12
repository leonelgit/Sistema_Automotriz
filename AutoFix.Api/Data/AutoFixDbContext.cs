using AutoFix.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Data;

public class AutoFixDbContext : DbContext
{
    public AutoFixDbContext(DbContextOptions<AutoFixDbContext> options) : base(options) { }

    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<Vehiculo> Vehiculos => Set<Vehiculo>();
    public DbSet<Mecanico> Mecanicos => Set<Mecanico>();
    public DbSet<AsignacionMecanico> Asignaciones => Set<AsignacionMecanico>();
    public DbSet<OrdenReparacion> Ordenes => Set<OrdenReparacion>();
    public DbSet<HistorialEstado> HistorialEstados => Set<HistorialEstado>();
    public DbSet<Presupuesto> Presupuestos => Set<Presupuesto>();
    public DbSet<DetallePresupuesto> DetallesPresupuesto => Set<DetallePresupuesto>();
    public DbSet<Repuesto> Repuestos => Set<Repuesto>();
    public DbSet<MovimientoInventario> Movimientos => Set<MovimientoInventario>();
    public DbSet<Evidencia> Evidencias => Set<Evidencia>();
    public DbSet<LogSolicitud> LogsSolicitudes => Set<LogSolicitud>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ── Usuarios
        b.Entity<Usuario>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.Property(x => x.Rol).HasMaxLength(30).IsRequired();
            e.HasOne(x => x.Cliente).WithMany().HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Mecanico).WithMany().HasForeignKey(x => x.MecanicoId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── Clientes y vehículos (uno a muchos)
        b.Entity<Cliente>(e =>
        {
            e.HasIndex(x => x.Dui).IsUnique();
            e.Property(x => x.NombreCompleto).HasMaxLength(120).IsRequired();
            e.Property(x => x.Dui).HasMaxLength(20).IsRequired();
        });

        b.Entity<Vehiculo>(e =>
        {
            e.HasIndex(x => x.Placa).IsUnique();
            e.HasIndex(x => x.NumeroChasis).IsUnique();
            e.HasOne(x => x.Cliente).WithMany(c => c.Vehiculos)
                .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Órdenes
        b.Entity<OrdenReparacion>(e =>
        {
            e.HasIndex(x => x.NumeroOrden).IsUnique();
            e.HasIndex(x => new { x.VehiculoId, x.Estado });
            e.HasOne(x => x.Cliente).WithMany(c => c.Ordenes).HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vehiculo).WithMany().HasForeignKey(x => x.VehiculoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Mecanico).WithMany().HasForeignKey(x => x.MecanicoId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Presupuesto).WithOne(p => p.Orden).HasForeignKey<Presupuesto>(p => p.OrdenId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<HistorialEstado>()
            .HasOne(x => x.Orden).WithMany(o => o.Historial).HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<AsignacionMecanico>(e =>
        {
            e.HasOne(x => x.Mecanico).WithMany(m => m.Asignaciones).HasForeignKey(x => x.MecanicoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Orden).WithMany(o => o.Asignaciones).HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MecanicoId, x.Activa });
        });

        // ── Presupuestos
        b.Entity<DetallePresupuesto>(e =>
        {
            e.HasOne(x => x.Presupuesto).WithMany(p => p.Detalles).HasForeignKey(x => x.PresupuestoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Repuesto).WithMany().HasForeignKey(x => x.RepuestoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Inventario
        b.Entity<Repuesto>(e =>
        {
            e.HasIndex(x => x.Codigo).IsUnique();
            // Última línea de defensa: la base de datos tampoco acepta existencias negativas.
            e.ToTable(t => t.HasCheckConstraint("CK_Repuestos_Existencias_NoNegativas", "\"Existencias\" >= 0"));
        });

        b.Entity<MovimientoInventario>(e =>
        {
            e.HasOne(x => x.Repuesto).WithMany().HasForeignKey(x => x.RepuestoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Orden).WithMany().HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.Fecha);
        });

        // ── Evidencias
        b.Entity<Evidencia>()
            .HasOne(x => x.Orden).WithMany(o => o.Evidencias).HasForeignKey(x => x.OrdenId).OnDelete(DeleteBehavior.Cascade);

        // ── Logs
        b.Entity<LogSolicitud>(e =>
        {
            e.HasIndex(x => x.CorrelationId);
            e.HasIndex(x => x.Fecha);
        });
    }

    /// <summary>Cambia el token de concurrencia de cada entidad modificada.</summary>
    private void RenovarTokensDeConcurrencia()
    {
        foreach (var entry in ChangeTracker.Entries<EntidadConcurrente>()
                     .Where(x => x.State == EntityState.Modified))
        {
            entry.Entity.Version = Guid.NewGuid();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RenovarTokensDeConcurrencia();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        RenovarTokensDeConcurrencia();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }
}
