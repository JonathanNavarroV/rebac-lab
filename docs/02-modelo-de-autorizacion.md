# 2. El modelo de autorización, relación por relación

Este documento explica **por qué** cada relación del modelo está donde está. El modelo vive en
[`PlaygroundModels.cs`](../backend/access-control/application/Authorization/Model/PlaygroundModels.cs)
y se puede ver renderizado en la pantalla **Modelo** del laboratorio.

## Las dos mitades del sistema

Antes de nada, una distinción que resuelve la mitad de las dudas:

| | El modelo | Las tuplas |
|---|---|---|
| Qué es | Cómo se derivan las relaciones | Los hechos |
| Tamaño | Cabe en un fichero | Millones (billones en Google) |
| Cambia | Una vez por release | Continuamente |
| Quién lo escribe | Un desarrollador | Los usuarios, al invitar o mover cosas |
| Ejemplo | `can_edit: editor or owner` | `project:alpha#editor@user:juan` |

Esa asimetría justifica una decisión de rendimiento importante: **el modelo se cachea en
memoria de forma agresiva y las tuplas no se cachean en absoluto**. Ver
[doc 06](./06-caching-consistencia-concurrencia.md).

## Todo objeto es `tipo:id`

```
user:juan     organization:acme     team:backend
group:seguridad     project:alpha     folder:docs     resource:a
```

Y **todas** las relaciones del sistema viven en **una sola tabla**. No hay
`project_permissions`, ni `folder_acl`, ni `user_roles`. Pertenencias a equipos, jerarquías,
comparticiones, bloqueos y propiedad son filas de la misma tabla, distinguidas solo por el
valor de la columna `relation`.

La consecuencia práctica: **añadir un tipo nuevo de recurso protegido no requiere ninguna
migración**. Se declara en el modelo, que es un dato.

## Los tres tipos de sujeto

Esto es lo que separa ReBAC de RBAC, y lo que más cuesta interiorizar:

```
project:alpha#editor@user:juan               ← un INDIVIDUO
project:alpha#editor@team:backend#member     ← un CONJUNTO (userset)
project:gamma#viewer@user:*                  ← un COMODÍN
```

La segunda forma es la interesante. No dice "estas personas pueden editar Alpha". Dice **"quien
sea miembro de Backend puede editar Alpha"**, y ese conjunto se evalúa *en el momento de
preguntar*. Metes a alguien en el equipo y hereda el acceso sin que nadie toque una sola tupla
de permisos.

**Ahí está la respuesta a "cómo se evita asignar permisos a millones de usuarios de uno en
uno".** Una tupla puede alcanzar a un conjunto arbitrariamente grande, y ese conjunto puede
crecer sin tocar la tupla.

## Las cinco reglas, y solo cinco

Todo el poder expresivo del modelo cabe en cinco reglas de reescritura. Si algo no se puede
expresar combinándolas, no se puede expresar en ReBAC.

| Regla | DSL | Qué resuelve |
|---|---|---|
| `_this` | `[user, team#member]` | Caso base: las tuplas escritas. **La única que consulta la BD** |
| `computed_userset` | `define can_edit: editor` | Otra relación del mismo objeto |
| `tuple_to_userset` | `define can_view: can_view from parent` | **Herencia y jerarquías** |
| `union` | `a or b` | Cualquiera basta → múltiples caminos |
| `intersection` / `exclusion` | `a and b` / `a but not b` | Exigir dos cosas / revocar |

## Por qué cada relación del modelo está donde está

### `organization.member: [user, team#member]`

Admite un userset de equipo. Con **cuatro tuplas** toda la plantilla de Acme es miembro de
Acme, hoy y en el futuro:

```
organization:acme#member@team:backend#member
organization:acme#member@team:frontend#member
organization:acme#member@team:management#member
```

Contrátese a alguien mañana y bastará meterle en su equipo. No hay ninguna tabla de "usuarios
de la organización" que mantener sincronizada.

> **Con RBAC:** N filas y un proceso que las mantenga al día. Y ese proceso es donde se cuelan
> los errores: alguien se va del equipo y sigue en la organización.

### `organization.admin: [user]`, separada de `member`

Están separadas porque conceden cosas distintas: los administradores heredan `can_edit` y
`can_delete` sobre todos los proyectos (vía `admin from parent`), los miembros no.

Es un patrón general que conviene interiorizar: **una relación por cada cosa distinta que se
quiera conceder**. Meterlo todo en `member` con un flag obligaría a preguntar por el flag, y
eso es volver a RBAC.

### `team` y `group`, dos formas de agrupar

La diferencia no es cosmética: `team` tiene `parent: [organization]` y `group` no. Un grupo
puede cruzar organizaciones (un grupo "Seguridad" con gente de Acme y de Globex), un equipo no.

Tenerlos ambos demuestra que **el modelo no impone una única forma de agrupar**: cada tipo
declara sus reglas. Y para el motor son idénticos — un objeto con una relación `member` usada
como userset. No hay ningún código especial para "equipos" ni para "grupos".

### `group.member: [user, group#member]` — grupos anidados

El propio tipo admitido como sujeto. Con eso, los grupos dentro de grupos salen gratis y a
cualquier profundidad: la misma recursión que resuelve todo lo demás.

También es donde se puede provocar un **ciclo** (A dentro de B, B dentro de A) para ver que el
motor lo detecta en lugar de colgarse. No es un caso rebuscado: es una operación de
administración perfectamente normal, y un motor sin detección de ciclos se cae por ella.

### `project.can_edit: editor or owner or admin from parent`

Tres cosas a la vez:

1. **`editor`** — el hecho directo.
2. **`owner`** — un `computed_userset`: quien sea propietario también puede editar.
3. **`admin from parent`** — un `tuple_to_userset`: sube por `parent` hasta la organización y
   pregunta `admin` allí.

Esa tercera cláusula es la que hace que **María pueda editar los cuatro proyectos de Acme sin
aparecer nombrada en ninguno**. Una tupla (`organization:acme#admin@user:maria`) alcanza todos
los proyectos presentes y futuros.

> **Con RBAC:** hay que enumerar cada proyecto en el rol `acme-admin`. Y volver a hacerlo cada
> vez que se cree uno nuevo. El [`RbacSeeder`](../backend/business/infrastructure/Rbac/RbacSeeder.cs)
> tiene ese bucle escrito, precisamente para que se vea.

### `folder.parent: [project, folder]` — la jerarquía recursiva

`parent` admite el **propio tipo**. Eso hace que esta línea:

```
define can_view: viewer or can_edit or can_view from parent
```

recorra el árbol completo, a cualquier profundidad. Un árbol de 50 niveles usa exactamente esta
misma línea. Bórrala y las carpetas dejan de heredar — eso es literalmente la única diferencia
entre los dos modelos publicados en el laboratorio, y se puede comprobar en dos clics.

> **Con RBAC:** hay que propagar los permisos hacia abajo al conceder, y **volver a
> propagarlos cada vez que algo se mueve de carpeta**. Aquí mover un recurso es reescribir una
> tupla y no hay nada que recalcular.

### `project.can_publish: can_edit and member from parent` — la intersección

Hay que poder editar **y** ser miembro de la organización. Modela al colaborador externo: se le
da `editor` sobre un proyecto concreto, pero no debe poder publicar porque no es de la casa.

En el laboratorio es Sofía: `can_edit` → ALLOW, `can_publish` → DENY.

> **Con RBAC:** aquí es donde empiezan a inventarse roles. `editor`,
> `editor-que-publica`, `editor-externo`… La combinatoria de condiciones se convierte en
> catálogo de roles, y cada condición nueva multiplica el catálogo.

### `resource.can_view: viewable but not blocked` — la exclusión

La rama excluida **gana siempre**. Da igual por cuántos caminos se conceda el acceso: una sola
tupla `blocked` los anula todos.

Tiene un coste que conviene conocer: **rompe la expansión inversa de `ListObjects`**. No se
puede saber a quién hay que restar sin comprobarlo objeto a objeto. Ver
[doc 04](./04-list-objects-y-rendimiento.md).

### `shared_with`, separada de `viewer`

Funcionalmente podrían ser lo mismo. Está aparte para poder responder *"¿esto lo ve por su rol
o porque alguien se lo compartió?"* — que es exactamente la pregunta de una revisión de
accesos. **La relación es el registro de la intención**, no solo del efecto.

## Hechos contra conclusiones

La convención más importante del modelo:

| Se escriben como tupla (hechos) | Se calculan (conclusiones) |
|---|---|
| `owner`, `editor`, `viewer` | `can_view`, `can_edit` |
| `member`, `admin` | `can_delete`, `can_publish` |
| `parent`, `shared_with`, `blocked` | `viewable` |

**El negocio pregunta siempre por las conclusiones.** Cuesta cuatro líneas de modelo y compra
muchísimo: el día que decidas que los administradores también borran proyectos, cambias
`can_delete` y no tocas ni una línea de negocio ni una sola tupla. Si el negocio preguntara por
`owner` directamente, ese cambio sería una migración de datos.

El sistema lo hace cumplir: intentar escribir `project:alpha#can_edit@user:juan` devuelve un
error explicando que es una relación derivada.

---

Anterior: [1. Qué problema resuelve](./01-que-problema-resuelve-rebac.md) ·
Siguiente: [3. Cómo funciona un Check](./03-como-funciona-check.md)
