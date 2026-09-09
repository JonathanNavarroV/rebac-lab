# Authorization Playground

Un laboratorio para **aprender ReBAC** con un motor estilo Zanzibar escrito desde cero, con la
traza de cada decisión a la vista.

No es una aplicación de negocio con permisos. Es un banco de pruebas donde cambias una relación
y ves inmediatamente qué decisiones cambian, por qué, y cuánto cuesta averiguarlo.

---

## Arranque rápido

```powershell
# 1. Las dos bases de datos
docker compose up -d

# 2. Los dos servicios, en dos terminales
dotnet run --project backend/access-control/presentation   # :15101
dotnet run --project backend/business/presentation         # :15100

# 3. El frontend
cd frontend && npm install && npm start                     # :14200
```

Las migraciones se aplican solas al arrancar y el escenario se siembra si la base está vacía.
No hay más pasos.

**Para volver al punto de partida** después de haber experimentado: el botón «Restaurar
escenario» de la pantalla de Relaciones, o `POST /access-control/seed/reset`. Solo borra las
tuplas; los modelos publicados se conservan.

**Para empezar de cero del todo:** `docker compose down -v && docker compose up -d`.

| Servicio | URL | Qué es |
|---|---|---|
| Frontend | http://localhost:14200 | El laboratorio |
| Módulo de control de acceso | http://localhost:15101/scalar/v1 | Check / Expand / ListObjects |
| Servicio de negocio | http://localhost:15100/scalar/v1 | Proyectos, recursos, comparación RBAC |

> **¿Solo quieres trastear con el motor?** El módulo arranca sin base de datos:
> `dotnet run --project backend/access-control/presentation --launch-profile access-control-in-memory`

---

## Por dónde empezar

1. **Casos guiados** (`/cases`) — 16 preguntas, cada una enseña algo distinto. Empieza aquí.
2. **Explorer** (`/explorer`) — la misma pregunta, con el árbol de evaluación completo.
3. **Relaciones** (`/relationships`) — crea una tupla y mira qué decisiones cambian.
4. **Listado autorizado** (`/objects`) — naive contra expansión inversa, con métricas.
5. **RBAC vs ReBAC** (`/comparison`) — la misma pregunta por los dos modelos.

Y en paralelo, la documentación:

| Documento | Responde a |
|---|---|
| [1. Qué problema resuelve ReBAC](docs/01-que-problema-resuelve-rebac.md) | Qué es Zanzibar, qué es ReBAC, y qué **no** resuelven |
| [2. El modelo de autorización](docs/02-modelo-de-autorizacion.md) | Por qué cada relación está donde está |
| [3. Cómo funciona un Check](docs/03-como-funciona-check.md) | El algoritmo, las salvaguardas, los múltiples caminos |
| [4. ListObjects y rendimiento](docs/04-list-objects-y-rendimiento.md) | El problema difícil, con números medidos |
| [5. ReBAC vs RBAC vs ABAC](docs/05-rebac-vs-rbac-vs-abac.md) | Cuándo usar cada uno, sin vender ninguno |
| [6. Caching, consistencia, concurrencia](docs/06-caching-consistencia-concurrencia.md) | Zookies, invalidación y lo que aquí **no** está |

---

## La arquitectura, y por qué

```
┌──────────────────────────────────────────────────────────┐
│  Frontend Angular 20                              :14200 │
└────────────┬────────────────────────────┬────────────────┘
             │                            │
┌────────────▼─────────────┐  ┌───────────▼────────────────┐
│  Servicio de NEGOCIO     │  │  MÓDULO DE CONTROL         │
│                  :15100  │  │  DE ACCESO         :15101  │
│                          │  │                            │
│  Proyectos, carpetas,    │  │  Check / Expand /          │
│  recursos, equipos       │  │  ListObjects               │
│                          │  │                            │
│  NO tiene ni una tabla   │──▶  NO sabe qué es un         │
│  de permisos             │  │  proyecto                  │
└────────────┬─────────────┘  └───────────┬────────────────┘
             │                            │
     postgres-business            postgres-access-control
          :15432                          :15433
```

**Dos bases de datos separadas, y no es decoración.** Al estar separadas es *imposible* hacer un
`JOIN` entre un proyecto y quién tiene acceso a él. Con todo en la misma base, la tentación de
resolver "qué proyectos puede ver Juan" con un `JOIN` de tres tablas sería irresistible,
funcionaría con el dataset pequeño, y no se aprendería nada de por qué existe `ListObjects`.

### El interruptor

```jsonc
// backend/business/presentation/appsettings.json
"Authorization": { "Mode": "InProcess" }   // o "Remote"
```

| | `InProcess` | `Remote` |
|---|---|---|
| Dónde corre el motor | Dentro del negocio | Servicio aparte, por HTTP |
| Latencia por check | ~0 ms | ~2-15 ms |
| ¿Hace falta caché? | No | Sí |
| ¿Existe el modo degradado? | No | Sí (y hay que decidir: fail open o closed) |
| ¿Tiene sentido la consistencia? | No | Sí — es el problema de los *zookies* |

Mismo modelo, mismas tuplas, mismas respuestas. **Distinta latencia y distintas garantías.**
Cambiar el modo, recargar el espacio de trabajo y mirar `checksPerformed` y `authorizationMs`
es el ejercicio.

---

## Qué se puede probar en cinco minutos

| Pregunta | Dónde |
|---|---|
| ¿Puede Juan editar Project Alpha? ¿Por qué? | Explorer |
| ¿Qué pasa si saco a Juan del equipo? | Relaciones → borra `team:backend#member@user:juan` |
| ¿Y si el equipo deja de tener acceso? | Relaciones → borra `project:alpha#editor@team:backend#member` |
| ¿Y si el proyecto cambia de organización? | Relaciones → reescribe la tupla `parent` |
| ¿Qué recursos puede ver Juan? ¿Y por qué uno sí y otro no? | Listado autorizado |
| ¿Cómo se representa una jerarquía? | Modelo → `folder.parent: [project, folder]` |
| ¿Cómo se heredan permisos? | Explorer → `user:pedro` / `can_view` / `resource:b` |
| ¿Qué pasa si desactivo la herencia? | Modelo → publica «Sin herencia en carpetas» |
| ¿Cómo se resuelven múltiples caminos? | Explorer → `user:juan` / `can_view` / `project:alpha` |
| ¿En qué se diferencia de RBAC? | RBAC vs ReBAC |
| ¿Cómo escala esto? | Listado autorizado → mira las métricas |

---

## El modelo, en una pantalla

```
type user

type organization
  relations
    define member: [user, team#member]
    define admin: [user]

type team
  relations
    define parent: [organization]
    define member: [user, team#member]

type group
  relations
    define member: [user, group#member]        # grupos anidados

type project
  relations
    define parent: [organization]
    define owner: [user]
    define editor: [user, team#member, group#member]
    define viewer: [user, team#member, group#member, organization#member, user:*]

    define can_view: viewer or can_edit
    define can_edit: editor or owner or admin from parent
    define can_delete: owner or admin from parent
    define can_publish: can_edit and member from parent        # intersección

type folder
  relations
    define parent: [project, folder]                           # recursivo
    define owner: [user]
    define viewer: [user, team#member, group#member]
    define editor: [user, team#member, group#member]

    define can_view: viewer or can_edit or can_view from parent
    define can_edit: editor or owner or can_edit from parent

type resource
  relations
    define parent: [project, folder]
    define owner: [user]
    define viewer: [user, team#member, group#member, organization#member, user:*]
    define editor: [user, team#member, group#member]
    define shared_with: [user, team#member, group#member, organization#member]
    define blocked: [user]

    define viewable: viewer or shared_with or can_edit or can_view from parent
    define can_view: viewable but not blocked                  # exclusión
    define can_edit: editor or owner or can_edit from parent
```

Es el DSL de **OpenFGA**, así que lo que aprendas aquí es transferible tal cual.

---

## Por qué motor propio y no OpenFGA

OpenFGA es excelente y en producción sería la elección correcta. Aquí no, por una razón muy
concreta: **su `Check` devuelve `{allowed: true}` y nada más**.

Sin la traza, el Explorer no tendría nada que mostrar, el grafo no podría resaltar caminos, no
se podrían contar los múltiples caminos y la auditoría guardaría un booleano. Los puntos más
didácticos del laboratorio serían imposibles.

Lo que sí se hace para que no sea un juguete arbitrario:

- El modelo se escribe en **el DSL real de OpenFGA**.
- Hay una **suite de conformidad** (85 tests) escrita contra la interfaz, no contra la
  implementación. Añadir un adaptador de OpenFGA sería heredar de esa clase.
- La estrategia naive de `ListObjects` sirve de **oráculo**: 30 combinaciones comprueban que la
  expansión inversa devuelve exactamente el mismo conjunto.

---

## El dataset

Cinco personas, cada una diseñada para demostrar algo:

| | Situación |
|---|---|
| **Juan** | Todo le llega por pertenecer a Team Backend. Y tiene **dos caminos** hacia Alpha |
| **Pedro** | Acumula accesos por vías distintas: equipo, grupo anidado, propiedad y una carpeta |
| **María** | Administradora de Acme. **No aparece en ningún proyecto** y puede editarlos todos |
| **Ana** | De Globex. Fuera de Acme solo tiene un recurso compartido. Y está **bloqueada** en otro |
| **Sofía** | Colaboradora externa: puede **editar y no publicar**. El caso que rompe RBAC |

42 tuplas en total. El mismo escenario en RBAC necesita ~130 filas — y el número lo calcula el
propio sistema en la pantalla de comparación.

---

## Estructura

```
poc-rebac/
├── docker-compose.yml            dos postgres
├── docs/                         los "por qué"
├── backend/
│   ├── shared/contracts/         DTOs compartidos
│   ├── access-control/           EL MÓDULO
│   │   ├── domain/               tuplas, usersets, contratos (sin dependencias)
│   │   ├── application/          EL MOTOR + parser del DSL + escenario
│   │   ├── infrastructure/       EF Core, índices forward/reverse
│   │   └── presentation/         API :15101
│   ├── business/                 EL NEGOCIO
│   │   ├── domain/               entidades SIN permisos + el puerto
│   │   ├── application/          CQRS
│   │   ├── infrastructure/       adaptadores InProcess/Remote + RBAC
│   │   └── presentation/         API :15100
│   └── tests/
│       ├── AccessControl.ConformanceTests/   ← los casos A–H
│       └── AccessControl.UnitTests/
└── frontend/                     Angular 20 + Tailwind 4 + Cytoscape
```

Los ficheros que más enseñan, en orden:

1. [`CheckEvaluator.cs`](backend/access-control/application/Authorization/Engine/CheckEvaluator.cs) — el algoritmo de Zanzibar
2. [`UsersetRewrite.cs`](backend/access-control/domain/Model/UsersetRewrite.cs) — las cinco reglas
3. [`SubjectRef.cs`](backend/access-control/domain/Model/SubjectRef.cs) — por qué un conjunto puede ocupar el sitio de una persona
4. [`ListObjectsReverseStrategy.cs`](backend/access-control/application/Authorization/Engine/ListObjectsReverseStrategy.cs) — el problema difícil
5. [`RbacSeeder.cs`](backend/business/infrastructure/Rbac/RbacSeeder.cs) — el bucle que RBAC obliga a escribir

---

## Tests

```powershell
cd backend
dotnet test
```

- **`AccessControl.ConformanceTests`** — qué debe responder cualquier motor estilo Zanzibar.
  Los 16 casos guiados, ciclos, profundidad, múltiples caminos, exclusión, intersección,
  comodines, grupos anidados, `Expand`, `BatchCheck` y la equivalencia naive↔inversa.
- **`AccessControl.UnitTests`** — el parser del DSL y su validación.

Corren **sin Docker y sin base de datos**: el motor solo conoce `IRelationshipTupleStore`, así
que la suite lo ejercita con un almacén en memoria. Los casos A–H son tests de milisegundos.

---

## Stack

.NET 10 · Clean Architecture · Minimal API · MediatR · FluentValidation · EF Core 10 ·
PostgreSQL 17 · Scalar · Angular 20 standalone + zoneless · Tailwind 4 · Cytoscape.js ·
xUnit + FluentAssertions
