using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NexoRuta.Mobile.Core.Jornadas;

namespace NexoRuta.Mobile.Storage.Tests;

public sealed class RepositorioJornadasSqliteTests : IDisposable
{
    private readonly string raiz = Path.Combine(Path.GetTempPath(), "nexoruta-sqlite-" + Guid.NewGuid().ToString("N"));
    private readonly Claves claves = new();
    private static readonly CuentaMovil Cuenta = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 10, 0, 0, TimeSpan.FromHours(-3));
    private static JornadaLocal Nueva() => JornadaLocal.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Cuenta, Ahora);
    private static JsonElement Datos() => JsonDocument.Parse("{\"receptor\":\"Persona sensible 12345678\"}").RootElement.Clone();

    [Fact]
    public async Task GuardaYReabreJornadaConEntregaSinTextoPlanoEnArchivo()
    {
        var jornada = Nueva();
        jornada.RegistrarEntregaFisica(Guid.NewGuid(), Datos(), Ahora.AddHours(1));
        using (var db = new RepositorioJornadasSqlite(raiz, claves)) await db.CrearAsync(jornada);
        using var reabierto = new RepositorioJornadasSqlite(raiz, claves);
        var restaurada = await reabierto.LeerAsync(Cuenta, jornada.Id);
        Assert.Equal(SerializadorJornadaLocal.Serializar(jornada), SerializadorJornadaLocal.Serializar(restaurada!));
        Assert.True(await reabierto.TienePendientesAsync(Cuenta));
        var bytes = await File.ReadAllBytesAsync(Archivo());
        Assert.False(bytes.AsSpan().StartsWith("SQLite format 3"u8));
        Assert.DoesNotContain("Persona sensible", Encoding.UTF8.GetString(bytes));
        await using var sinClave = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Archivo(), Pooling = false }.ToString());
        await sinClave.OpenAsync();
        using var leer = sinClave.CreateCommand();
        leer.CommandText = "SELECT * FROM Jornadas";
        await Assert.ThrowsAsync<SqliteException>(async () => await leer.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ErrorDuranteModificacionRevierteTodoSinPerderOperacionAnterior()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        await db.CrearAsync(jornada);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ModificarAsync(Cuenta, jornada.Id, j =>
        {
            j.RegistrarEntregaFisica(Guid.NewGuid(), Datos(), Ahora.AddHours(1));
            throw new InvalidOperationException("Captura interrumpida");
        }));
        Assert.Single((await db.LeerAsync(Cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task ModificacionesConcurrentesConservanTodasLasOperacionesYSecuencias()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        await db.CrearAsync(jornada);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => db.ModificarAsync(Cuenta, jornada.Id,
            j => j.RegistrarEntregaFisica(Guid.NewGuid(), Datos(), Ahora.AddHours(1)))));
        var restaurada = (await db.LeerAsync(Cuenta, jornada.Id))!;
        Assert.Equal(Enumerable.Range(1, 9).Select(x => (long)x), restaurada.Operaciones.Select(x => x.Secuencia));
        Assert.Equal(9, restaurada.Operaciones.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task ClavePerdidaNoGeneraOtraNiModificaArchivo()
    {
        var jornada = Nueva();
        using (var db = new RepositorioJornadasSqlite(raiz, claves)) await db.CrearAsync(jornada);
        var antes = SHA256.HashData(await File.ReadAllBytesAsync(Archivo()));
        claves.Valores.Clear();
        using var reabierto = new RepositorioJornadasSqlite(raiz, claves);
        await Assert.ThrowsAsync<ClaveLocalNoDisponibleException>(() => reabierto.LeerAsync(Cuenta, jornada.Id));
        Assert.Empty(claves.Valores);
        Assert.Equal(antes, SHA256.HashData(await File.ReadAllBytesAsync(Archivo())));
    }

    [Fact]
    public async Task ClaveIncorrectaNoBorraLaBaseNiLaReinicializa()
    {
        var jornada = Nueva();
        using (var db = new RepositorioJornadasSqlite(raiz, claves)) await db.CrearAsync(jornada);
        var nombre = Assert.Single(claves.Valores).Key;
        var correcta = claves.Valores[nombre];
        claves.Valores[nombre] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        using var reabierto = new RepositorioJornadasSqlite(raiz, claves);
        await Assert.ThrowsAsync<SqliteException>(() => reabierto.LeerAsync(Cuenta, jornada.Id));
        claves.Valores[nombre] = correcta;
        Assert.Single((await reabierto.LeerAsync(Cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task OtraCuentaNoPuedeLeerJornadaNiHojaDeLaPrimera()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        await db.CrearAsync(jornada);
        var otra = new CuentaMovil(Cuenta.UsuarioId, Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(await db.LeerAsync(otra, jornada.Id));
        Assert.False(await db.TienePendientesAsync(otra));
        Assert.True(await db.TienePendientesAsync(Cuenta));
        Assert.Equal(2, claves.Valores.Count);
    }

    [Fact]
    public async Task CopiarBaseYClaveAotraCuentaNoCambiaSuPropietario()
    {
        var jornada = Nueva();
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        await db.CrearAsync(jornada);
        var original = Archivo();
        var claveOriginal = Assert.Single(claves.Valores).Value;
        var otra = new CuentaMovil(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await db.TienePendientesAsync(otra);
        var otraRuta = Directory.GetFiles(raiz, "operacion.sqlite", SearchOption.AllDirectories).Single(x => x != original);
        var otraClave = claves.Valores.Single(x => x.Value != claveOriginal).Key;
        File.Copy(original, otraRuta, overwrite: true);
        claves.Valores[otraClave] = claveOriginal;
        await Assert.ThrowsAsync<ClaveLocalNoDisponibleException>(() => db.LeerAsync(otra, jornada.Id));
    }

    [Fact]
    public async Task HojaNoSeReemplazaConJornadaActivaOPendientesYSigueLegibleTrasError()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        var contenido = Encoding.UTF8.GetBytes("{\"version\":1,\"direccion\":\"Domicilio reservado\"}");
        await db.GuardarHojaAsync(Cuenta, jornada.RutaId, contenido);
        await db.CrearAsync(jornada);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.GuardarHojaAsync(Cuenta, jornada.RutaId, "{}"u8.ToArray()));
        Assert.Equal(contenido, await db.LeerHojaAsync(Cuenta, jornada.RutaId));
        await db.ModificarAsync(Cuenta, jornada.Id, j =>
        {
            j.Finalizar(Ahora.AddHours(8));
            foreach (var op in j.Operaciones) j.ConfirmarPersistencia(op.Id);
        });
        Assert.False(await db.TienePendientesAsync(Cuenta));
        await db.GuardarHojaAsync(Cuenta, jornada.RutaId, "{}"u8.ToArray());
        Assert.Equal("{}"u8.ToArray(), await db.LeerHojaAsync(Cuenta, jornada.RutaId));
    }

    [Fact]
    public async Task CrearEsIdempotenteSinPermitirReemplazarPendientes()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        await db.CrearAsync(jornada);
        await db.CrearAsync(jornada);
        jornada.RegistrarEntregaFisica(Guid.NewGuid(), Datos(), Ahora.AddHours(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.CrearAsync(jornada));
        Assert.Single((await db.LeerAsync(Cuenta, jornada.Id))!.Operaciones);
    }

    [Fact]
    public async Task CancelacionAntesDeEscrituraNoAgregaOperacion()
    {
        using var db = new RepositorioJornadasSqlite(raiz, claves);
        var jornada = Nueva();
        await db.CrearAsync(jornada);
        using var cancelado = new CancellationTokenSource();
        cancelado.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.ModificarAsync(Cuenta, jornada.Id,
            j => j.Finalizar(Ahora.AddHours(8)), cancelado.Token));
        Assert.False((await db.LeerAsync(Cuenta, jornada.Id))!.EstaCerrada);
    }

    [Fact]
    public async Task SobrePermaneceInmutableTrasReaperturaYLaConfirmacionEsAtomicaConLaJornada()
    {
        var jornada = Nueva(); var operacion = jornada.Operaciones[0]; var sobre = "{\"id\":1}"u8.ToArray();
        using (var db = new RepositorioJornadasSqlite(raiz, claves))
        {
            await db.CrearAsync(jornada);
            await db.GuardarSobreAsync(Cuenta, jornada.Id, operacion.Id, sobre);
        }
        using var reabierto = new RepositorioJornadasSqlite(raiz, claves);
        Assert.Equal(sobre, await reabierto.LeerSobreAsync(Cuenta, jornada.Id, operacion.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reabierto.GuardarSobreAsync(Cuenta, jornada.Id, operacion.Id, "{}"u8.ToArray()));
        Assert.True((await reabierto.LeerAsync(Cuenta, jornada.Id))!.TienePendientes);
        var respuesta = "{\"resultado\":\"Aplicada\"}"u8.ToArray();
        await reabierto.ConfirmarSobreAsync(Cuenta, jornada.Id, operacion.Id, respuesta);
        Assert.False((await reabierto.LeerAsync(Cuenta, jornada.Id))!.TienePendientes);
        Assert.Equal(respuesta, Assert.Single(await reabierto.LeerConfirmacionesAsync(Cuenta, jornada.Id)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => reabierto.ConfirmarSobreAsync(Cuenta, jornada.Id, operacion.Id, "{}"u8.ToArray()));
        Assert.Equal(respuesta, Assert.Single(await reabierto.LeerConfirmacionesAsync(Cuenta, jornada.Id)));
    }

    [Fact]
    public async Task CierreNoSePreparaSinConfirmarLasOperacionesPreviasNiSeConfirmaSinSobreDurable()
    {
        var jornada = Nueva(); var cierre = jornada.Finalizar(Ahora.AddHours(8));
        using var db = new RepositorioJornadasSqlite(raiz, claves); await db.CrearAsync(jornada);
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.GuardarSobreAsync(Cuenta, jornada.Id, cierre.Id, "{}"u8.ToArray()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.ConfirmarSobreAsync(Cuenta, jornada.Id, jornada.Operaciones[0].Id, "{}"u8.ToArray()));
        Assert.Equal(2, (await db.LeerAsync(Cuenta, jornada.Id))!.ExportarEstado().Operaciones.Count);
        Assert.Empty((await db.LeerAsync(Cuenta, jornada.Id))!.ExportarEstado().Confirmadas);
    }

    private string Archivo() => Assert.Single(Directory.GetFiles(raiz, "operacion.sqlite", SearchOption.AllDirectories));
    public void Dispose() { if (Directory.Exists(raiz)) Directory.Delete(raiz, true); }

    private sealed class Claves : IAlmacenClavesProtegidas
    {
        public Dictionary<string, string> Valores { get; } = [];
        public Task<string?> ObtenerAsync(string nombre) => Task.FromResult(Valores.GetValueOrDefault(nombre));
        public Task GuardarAsync(string nombre, string valor) { Valores[nombre] = valor; return Task.CompletedTask; }
    }
}
