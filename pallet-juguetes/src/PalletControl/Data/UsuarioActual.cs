using System.Security.Claims;

namespace PalletControl.Data;

/// <summary>Nombre del usuario de la petición en curso, para la auditoría y el kárdex.</summary>
public interface IUsuarioActual { string Nombre { get; } }

public class UsuarioActualHttp(IHttpContextAccessor http) : IUsuarioActual
{
    public string Nombre => http.HttpContext?.User.FindFirstValue(ClaimTypes.Name) ?? "sistema";
}

public class UsuarioSistema : IUsuarioActual { public string Nombre => "sistema"; }
