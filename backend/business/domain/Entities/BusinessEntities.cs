namespace Playground.Business.Domain.Entities;

/// <summary>
/// Base de las entidades de negocio.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lo importante de estas entidades es lo que NO tienen.</b> Ni <c>OwnerId</c>, ni
/// <c>IsPublic</c>, ni <c>AccessLevel</c>, ni una colección <c>Permissions</c>. Ninguna
/// entidad de negocio sabe quién puede tocarla.
/// </para>
/// <para>
/// Es más raro de lo que parece: en la mayoría de aplicaciones, <c>Project</c> tendría al
/// menos un <c>CreatedByUserId</c> que además se usa para decidir permisos ("si eres el
/// creador, puedes borrarlo"). Aquí ese hecho vive donde le corresponde, como tupla
/// <c>project:alpha#owner@user:pedro</c>, y el negocio ni lo consulta ni lo necesita.
/// </para>
/// <para>
/// El identificador es una cadena y no un entero, a diferencia de tus otros proyectos. Es
/// deliberado: el id del negocio y el id del objeto en las tuplas tienen que ser el mismo
/// (<c>project:alpha</c>), y cadenas legibles hacen que las tuplas se puedan leer sin
/// traducir nada.
/// </para>
/// </remarks>
public abstract class BusinessEntity
{
    public string Id { get; set; } = null!;

    public string Name { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>El tipo tal y como aparece en las tuplas: <c>project</c>, <c>folder</c>...</summary>
    public abstract string ObjectType { get; }

    /// <summary>Referencia en notación de tupla: <c>project:alpha</c>.</summary>
    public string ObjectRef => $"{ObjectType}:{Id}";
}

/// <summary>
/// Una persona. En el laboratorio no hay contraseñas: se cambia de identidad con un selector.
/// </summary>
public sealed class BusinessUser : BusinessEntity
{
    public override string ObjectType => "user";

    /// <summary>Frase que resume su situación. Se muestra en el selector "actuar como".</summary>
    public string? Story { get; set; }
}

public sealed class Organization : BusinessEntity
{
    public override string ObjectType => "organization";
}

public sealed class Team : BusinessEntity
{
    public override string ObjectType => "team";

    /// <summary>
    /// Organización a la que pertenece.
    /// </summary>
    /// <remarks>
    /// Este dato existe <b>dos veces</b> en el sistema: aquí, como hecho de negocio, y en el
    /// módulo de control de acceso, como tupla <c>team:backend#parent@organization:acme</c>.
    /// Y esa duplicación es real, no un descuido del laboratorio: es el problema práctico más
    /// habitual al integrar un sistema tipo Zanzibar, porque las dos copias pueden
    /// desincronizarse. Ver <c>docs/06</c>.
    /// </remarks>
    public string? OrganizationId { get; set; }
}

public sealed class Group : BusinessEntity
{
    public override string ObjectType => "group";
}

public sealed class Project : BusinessEntity
{
    public override string ObjectType => "project";

    public string? OrganizationId { get; set; }

    public string? Description { get; set; }
}

/// <summary>Carpeta. Su padre puede ser un proyecto u otra carpeta.</summary>
public sealed class Folder : BusinessEntity
{
    public override string ObjectType => "folder";

    /// <summary>Referencia del padre en notación de objeto: <c>project:alpha</c> o <c>folder:docs</c>.</summary>
    public string? ParentRef { get; set; }
}

/// <summary>
/// Recurso: la hoja de la jerarquía.
/// </summary>
/// <remarks>
/// Se llama <c>ResourceItem</c> y no <c>Resource</c> para no chocar con
/// <c>System.Resources</c> ni con los <c>Resource</c> de ASP.NET, que aparecen por todas
/// partes con <c>ImplicitUsings</c> activado.
/// </remarks>
public sealed class ResourceItem : BusinessEntity
{
    public override string ObjectType => "resource";

    public string? ParentRef { get; set; }

    public string? Content { get; set; }
}
