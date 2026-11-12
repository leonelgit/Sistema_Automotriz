using AutoFix.Api.Entities;
using AutoFix.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoFix.Api.Data;

/// <summary>
/// Crea datos de prueba la primera vez (un usuario por rol, mecánicos, un cliente con vehículos y repuestos).
/// Las contraseñas son SOLO de prueba y están documentadas en el README.
/// </summary>
public static class DataSeeder
{
    public static async Task SeedAsync(AutoFixDbContext db, IPasswordHasher<Usuario> hasher, ILogger logger)
    {
        if (await db.Usuarios.AnyAsync()) return;

        logger.LogInformation("Base de datos vacía: creando datos de prueba...");

        // Mecánicos
        var carlos = new Mecanico { Nombre = "Carlos Martínez", Especialidad = "Motor y transmisión", Telefono = "7012-3456" };
        var jose = new Mecanico { Nombre = "José Hernández", Especialidad = "Frenos y suspensión", Telefono = "7098-7654" };
        var ana = new Mecanico { Nombre = "Ana López", Especialidad = "Sistema eléctrico", Telefono = "7111-2222" };
        db.Mecanicos.AddRange(carlos, jose, ana);

        // Cliente de demostración con dos vehículos
        var cliente = new Cliente
        {
            NombreCompleto = "María Fernanda Rivas", Dui = "01234567-8", Telefono = "7555-1234",
            Correo = "cliente@autofix.com", Direccion = "Col. Escalón, San Salvador"
        };
        cliente.Vehiculos.Add(new Vehiculo { Placa = "P123-456", Marca = "Toyota", Modelo = "Corolla", Anio = 2018, Color = "Gris", NumeroChasis = "JTDBR32E180012345" });
        cliente.Vehiculos.Add(new Vehiculo { Placa = "P789-012", Marca = "Honda", Modelo = "CR-V", Anio = 2021, Color = "Blanco", NumeroChasis = "5J6RW2H50ML001122" });
        db.Clientes.Add(cliente);

        // Repuestos (Pastillas de freno con 2 unidades: ideal para el escenario A)
        db.Repuestos.AddRange(
            new Repuesto { Codigo = "FRN-001", Nombre = "Pastillas de freno delanteras", Categoria = "Frenos", Marca = "Bosch", Precio = 45.00m, Existencias = 2, StockMinimo = 1, Descripcion = "Juego de 4 pastillas cerámicas" },
            new Repuesto { Codigo = "ACE-010", Nombre = "Aceite de motor 5W-30 (galón)", Categoria = "Lubricantes", Marca = "Mobil", Precio = 32.50m, Existencias = 20, StockMinimo = 5 },
            new Repuesto { Codigo = "FIL-020", Nombre = "Filtro de aceite", Categoria = "Filtros", Marca = "Fram", Precio = 8.75m, Existencias = 15, StockMinimo = 4 },
            new Repuesto { Codigo = "BAT-100", Nombre = "Batería 12V 45Ah", Categoria = "Eléctrico", Marca = "LTH", Precio = 110.00m, Existencias = 3, StockMinimo = 2 },
            new Repuesto { Codigo = "BUJ-030", Nombre = "Bujía de iridio", Categoria = "Encendido", Marca = "NGK", Precio = 12.00m, Existencias = 16, StockMinimo = 8 });

        await db.SaveChangesAsync();

        // Usuarios (uno por rol + un segundo supervisor SIN el claim Ordenes.Aprobar para demostrar 403 por claim)
        Usuario U(string nombre, string email, string rol, string password, string[]? permisos = null,
                  int? clienteId = null, int? mecanicoId = null)
        {
            var u = new Usuario
            {
                NombreCompleto = nombre, Email = email, Rol = rol,
                Permisos = string.Join(',', permisos ?? Permisos.PorDefecto(rol)),
                ClienteId = clienteId, MecanicoId = mecanicoId
            };
            u.PasswordHash = hasher.HashPassword(u, password);
            return u;
        }

        db.Usuarios.AddRange(
            U("Administrador AutoFix", "admin@autofix.com", Roles.Administrador, "Admin123!"),
            U("Rosa Recepción", "recepcion@autofix.com", Roles.Recepcionista, "Recepcion123!"),
            U("Carlos Martínez", "mecanico@autofix.com", Roles.Mecanico, "Mecanico123!", mecanicoId: carlos.Id),
            U("José Hernández", "mecanico2@autofix.com", Roles.Mecanico, "Mecanico123!", mecanicoId: jose.Id),
            U("Sofía Supervisora", "supervisor@autofix.com", Roles.Supervisor, "Supervisor123!"),
            U("Diego Supervisor (sin permiso de aprobar)", "supervisor2@autofix.com", Roles.Supervisor, "Supervisor123!",
                permisos: new[] { Permisos.OrdenesEditar, Permisos.OrdenesAsignar, Permisos.OrdenesFinalizar, Permisos.ReportesConsultar }),
            U(cliente.NombreCompleto, "cliente@autofix.com", Roles.Cliente, "Cliente123!", clienteId: cliente.Id));

        await db.SaveChangesAsync();
        logger.LogInformation("Datos de prueba creados.");
    }
}
