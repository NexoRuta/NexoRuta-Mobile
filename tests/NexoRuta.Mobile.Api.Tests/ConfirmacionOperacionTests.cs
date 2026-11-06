using System.Text.Json;
using NexoRuta.Mobile.Api.Reparto;

namespace NexoRuta.Mobile.Api.Tests;

public sealed class ConfirmacionOperacionTests
{
    [Theory]
    [InlineData("")]
    [InlineData(",\"versionesBulto\":null")]
    public void HistoricalNonParcelReceiptPreservesMetadataAndAnEmptyVersionMap(string field)
    {
        var id = Guid.NewGuid();
        var json = $"{{\"id\":\"{id}\",\"secuencia\":1,\"resultado\":\"Aplicada\",\"versionRuta\":2{field}}}";
        var ack = JsonSerializer.Deserialize(json, ContratosRepartoJson.Default.ConfirmacionOperacion)!;
        Assert.Equal(id, ack.Id); Assert.Equal(1, ack.Secuencia); Assert.Equal("Aplicada", ack.Resultado);
        Assert.Equal(2, ack.VersionRuta); Assert.Empty(ack.VersionesBulto);
        // Operation-specific validation belongs to synchronization, not this transport DTO.
    }
}
