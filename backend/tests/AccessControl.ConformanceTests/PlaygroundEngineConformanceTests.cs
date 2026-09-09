using Playground.AccessControl.Application.Authorization.Engine;
using Playground.AccessControl.Application.Authorization.Storage;
using Playground.AccessControl.Domain.Abstractions;

namespace AccessControl.ConformanceTests;

/// <summary>
/// La suite de conformidad ejecutada contra el motor de este laboratorio, con almacenes en
/// memoria.
/// </summary>
/// <remarks>
/// <para>
/// Fíjate en lo que se está probando: el <b>motor real</b>, el mismo código que atiende las
/// peticiones HTTP. Lo único sustituido es de dónde vienen las tuplas, y eso es posible
/// porque el motor solo conoce <see cref="IRelationshipTupleStore"/>. Sin esa separación,
/// estos tests necesitarían Docker, una base de datos y migraciones, y en la práctica
/// nadie los ejecutaría en cada cambio.
/// </para>
/// <para>
/// Para añadir un motor alternativo (OpenFGA, SpiceDB) basta con otra clase como esta que
/// sepa arrancarlo y cargarle el modelo y las tuplas. Toda la especificación se hereda.
/// </para>
/// </remarks>
public sealed class PlaygroundEngineConformanceTests : AccessControlConformanceTests
{
    protected override Task<IAccessControlEngine> CreateEngineAsync(string dsl, IEnumerable<string> tuples)
    {
        var tupleStore = new InMemoryRelationshipTupleStore(tuples.ToArray());
        var modelStore = new InMemoryAuthorizationModelStore(dsl);
        var catalog = new TupleDerivedObjectCatalog(tupleStore);

        IAccessControlEngine engine = new AccessControlEngine(tupleStore, modelStore, catalog);

        return Task.FromResult(engine);
    }
}
