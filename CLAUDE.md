# CLAUDE.md — Authorization Playground

Reglas de este repositorio. Complementan a `~/.claude/CLAUDE.md`, que sigue aplicando salvo en
las desviaciones documentadas abajo.

## Qué es esto

Un **laboratorio para aprender ReBAC/Zanzibar**, no una aplicación de negocio. La regla que
manda sobre todas las demás:

> **La claridad del modelo de autorización va por delante de cualquier otra consideración.**

Si una decisión hace el código más idiomático pero el modelo menos visible, gana el modelo. Si
una funcionalidad de negocio no enseña nada sobre autorización, no se implementa.

## Comandos

```powershell
docker compose up -d                                          # las dos bases

cd backend
dotnet build Playground.slnx
dotnet test                                                   # 105 tests, sin Docker

dotnet run --project access-control/presentation              # :15101
dotnet run --project business/presentation                    # :15100
dotnet run --project access-control/presentation --launch-profile access-control-in-memory

cd frontend
npm start                                                     # :14200
npx ng build
```

Puertos: **14200** frontend · **15100** negocio · **15101** control de acceso ·
**15432** postgres-business · **15433** postgres-access-control.

Están deliberadamente en un rango alto: los de trabajo (psinet) ocupan 4200, 5100-5177 y
5432-5438, y los dos entornos tienen que poder estar levantados a la vez. No bajarlos.

## Arquitectura: la regla no negociable

```
Business.Application  ──✗──▶  AccessControl.*
```

El negocio **no puede** referenciar el módulo de control de acceso. Solo conoce el puerto
`IAccessControlService`, que define él mismo en su dominio. La única costura está en
`Business.Infrastructure`, donde viven los dos adaptadores (`InProcess` y `Remote`).

Si algún cambio necesita que un handler de negocio sepa qué es una tupla, un userset o una
relación derivada, el cambio está mal planteado.

Dependencias hacia adentro dentro de cada mitad: `Domain ← Application ← Infrastructure /
Presentation`.

## Desviaciones conscientes respecto a psinet

Todas tienen motivo. No "arreglarlas" sin releer el motivo.

| Desviación | Por qué |
|---|---|
| **Sin MediatR en el módulo de control de acceso.** Los endpoints llaman al motor directamente | Un handler por endpoint solo reenviaría: no hay nada que orquestar. En el negocio **sí** se usa CQRS con MediatR, como siempre |
| **Comandos agrupados por feature** en lugar de un fichero por clase | Los cuatro comandos de proyecto hacen lo mismo con la autorización y solo cambia la relación. Juntos se ve; repartidos en doce ficheros, no |
| **`Directory.Build.props`** para `TargetFramework`, `Nullable` e `ImplicitUsings` | Una sola solución con 12 proyectos. Las **versiones de paquetes siguen por csproj**, como en psinet |
| **Ids `string` en lugar de `int`** | El id del negocio y el id en las tuplas deben ser el mismo (`project:alpha`). Cadenas legibles hacen que las tuplas se lean sin traducir |
| **Sin FK de `parent`/`organization`** entre entidades | La jerarquía real vive como tupla, en otra base de datos. Duplicarla como FK invitaría a resolver la autorización con un `JOIN` |
| **Comentarios largos, explicando el porqué** | Es documentación ejecutable: el objetivo del repo es enseñar. En otros repos serían excesivos; aquí son el producto |
| **`ForbiddenException` devuelve el motivo al cliente** | En producción sería fuga de información (revela el organigrama). Aquí es donde está el aprendizaje. Está avisado en el propio handler |

## Reglas específicas

### El modelo de autorización

- Vive en `PlaygroundModels.cs`. **Sin paréntesis** en las expresiones: la precedencia
  (`and` > `or` > `but not`) basta, y así es válido tal cual en OpenFGA.
- Cuando haga falta mezclar exclusión con unión, se introduce una relación intermedia (como
  `viewable` en `resource`), que es lo que OpenFGA obliga a hacer.
- **Separar hechos de conclusiones.** `owner`, `editor`, `member`, `parent` se escriben.
  `can_view`, `can_edit`, `can_delete` se calculan y **nunca** se escriben como tupla.
- El negocio pregunta siempre por las conclusiones (`can_*`), nunca por los hechos.

### El motor

- `CheckEvaluator` es el fichero más importante del repo. Cualquier cambio ahí tiene que
  mantener las tres salvaguardas: profundidad, ciclos y memoización por petición.
- **Nunca memoizar un `false` producido por un ciclo o por el límite de profundidad.** No son
  conclusiones sobre el problema.
- Toda lectura del almacén pasa por `EvaluationContext.ReadAsync` para que las métricas no se
  queden desfasadas.
- El motor solo conoce `IRelationshipTupleStore`. Si necesita EF Core, está en la capa
  equivocada — y la suite de conformidad dejaría de poder correr sin Docker.

### El escenario

- `PlaygroundScenario` es la fuente única: alimenta el seed de tuplas, el seed de negocio, los
  tests de conformidad y los casos guiados del frontend.
- **Cada tupla del escenario existe para demostrar algo concreto**, y lleva su comentario
  explicándolo. No añadir tuplas de relleno.
- Añadir un caso guiado nuevo = añadirlo a `Cases`. Aparece automáticamente en los tests, en el
  frontend y en el diff de "decisiones afectadas".

### Tests

- xUnit + Moq + FluentAssertions + AutoFixture, naming `Handle_<Scenario>_<ExpectedResult>`.
- La **suite de conformidad** se escribe contra `IAccessControlEngine`, nunca contra la
  implementación. Es lo que permitiría enchufar OpenFGA y heredar la especificación.
- Los tests no tocan Docker ni bases de datos reales.
- El test `ListObjects_ReverseExpansion_AgreesWithTheNaiveOracle` es el que sostiene la
  credibilidad de la expansión inversa. Si falla, el bug está en la inversa.

### Migraciones EF

Como siempre: **nunca a mano**, ni el Designer ni el Snapshot. Solo se tocan entidades y
configuraciones. Hay dos `DbContext`, así que `--context` es obligatorio:

```powershell
dotnet ef migrations add <Nombre> --project access-control/infrastructure --startup-project access-control/presentation --context AccessControlDbContext --output-dir Persistence/Migrations
dotnet ef migrations add <Nombre> --project business/infrastructure --startup-project business/presentation --context BusinessDbContext --output-dir Persistence/Migrations
```

Requiere `dotnet-ef` 10.x (`dotnet tool update --global dotnet-ef --version 10.0.*`). Las
migraciones se aplican solas al arrancar cada servicio.

Al tocar `RelationshipTupleConfiguration`, comprobar que la migración conserva
`.Annotation("Npgsql:NullsDistinct", false)` en el índice único: sin eso, PostgreSQL considera
distintos dos `NULL` y se pueden duplicar tuplas de sujetos individuales.

### Frontend

- Angular 20 standalone, **zoneless**: todo el estado que repinta es `signal`.
- Tailwind 4, `OnPush` en todos los componentes, rutas lazy.
- Los colores por tipo de objeto (`object-kind.ts`) son los mismos en el grafo, la traza, los
  chips y las listas. No inventar colores nuevos por pantalla.
- **Mostrar siempre las métricas.** En autorización el coste no es un detalle: tenerlo delante
  mientras experimentas es lo que construye la intuición.

## Al añadir algo nuevo

1. ¿Enseña algo sobre autorización? Si no, probablemente no vaya aquí.
2. ¿Necesita que el negocio conozca el modelo de autorización? Entonces está mal planteado.
3. ¿Hay un caso guiado que lo demuestre? Añádelo a `PlaygroundScenario.Cases`.
4. ¿Se explica el porqué en `docs/`? Si es una decisión no obvia, sí.
