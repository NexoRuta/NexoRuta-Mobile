namespace NexoRuta.Mobile.Api.Identidad;

// Clase deliberadamente sin ToString generado: contiene credenciales.
public sealed class TokensSesionMovil
{
    public required string AccessToken { get; init; }
    public required string TokenType { get; init; }
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    public required string RefreshToken { get; init; }
    public required DateTimeOffset RefreshExpiresAtUtc { get; init; }
    public required Guid SesionId { get; init; }

    internal bool EsValido => TokenType == "Bearer" && SesionId != Guid.Empty
        && !string.IsNullOrWhiteSpace(AccessToken) && AccessToken.Length <= 8192
        && !AccessToken.Any(char.IsWhiteSpace)
        && RefreshToken is { Length: 43 } && RefreshToken.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        && ExpiresAtUtc != default && ExpiresAtUtc <= RefreshExpiresAtUtc
        && ExpiresAtUtc.Offset == TimeSpan.Zero && RefreshExpiresAtUtc.Offset == TimeSpan.Zero;
}
