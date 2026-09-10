# 07 — Tour guiado: aprende el motor preguntándole

> **Hay una versión interactiva de esto en http://localhost:14200/tour**, y para aprender es
> mejor. Allí eliges la respuesta y el sistema **ejecuta la pregunta contra el motor** antes de
> explicarte nada: ves el árbol de evaluación y las métricas de esa ejecución concreta, sobre
> las tuplas que tengas en ese momento. Además lleva la cuenta de lo que llevas acertado.
>
> Este documento sigue siendo útil para leer sin levantar nada, o para ver los comandos `curl`
> equivalentes. Las dos versiones salen de la misma definición
> ([`TourQuestions.cs`](../backend/access-control/application/Authorization/Seed/TourQuestions.cs)),
> así que no pueden contradecirse.

Este documento es un recorrido en forma de **preguntas de predicción**. La mecánica es
siempre la misma: lee el contexto, decide qué crees que responderá el motor, y solo
entonces ejecuta el comando. Acertar enseña menos que fallar, así que conviene apostar de
verdad antes de mirar.

Las respuestas están plegadas al final de cada lección (`<details>`). No las abras antes.

Todo lo que hay aquí corre contra tu propio motor con el escenario que ya viene sembrado:
las 42 tuplas de `PlaygroundScenario`. No hace falta preparar nada más.

## Antes de empezar

```powershell
docker compose up -d
cd backend
dotnet run --project access-control/presentation   # :15101
dotnet run --project business/presentation         # :15100
```

Comprobación rápida: `curl http://localhost:15101/health` debe devolver `{"status":"healthy"}`.

Al final de este documento hay un pequeño script (`tour.py`) que formatea las trazas como
árbol; es opcional, pero hace las lecciones mucho más legibles que leer el JSON crudo.

## Los modelos publicados

| Id | Modelo |
|---|---|
| `eef2f49e9255eb88` | Modelo completo del laboratorio |
| `4301a6b8c7ae7d90` | Sin herencia en carpetas |
| `ed3009f4065f67a4` | RBAC expresado en ReBAC |

Los ids se regeneran si vuelves a sembrar desde cero: `curl http://localhost:15101/access-control/models`.

---

## Lo que ya está cubierto (lecciones 1–3)

Resumen para retomar el hilo, no hace falta releerlo entero.

**1. La tupla y el Check.** Todo el sistema es una tabla de hechos con la forma
`objeto#relación@sujeto`. El sujeto puede ser una persona (`user:juan`) o **un conjunto**
(`team:backend#member`), y esa segunda forma — el *userset* — es lo que separa ReBAC de una
tabla de permisos: una sola fila cubre a quien esté hoy y mañana en el equipo. La pregunta
que se le hace al motor son tres cadenas y nada más: sujeto, relación, objeto.

Distinción que el modelo respeta a rajatabla: `owner`, `editor`, `member`, `parent` son
**hechos** (se escriben); `can_view`, `can_edit`, `can_delete` son **conclusiones** (se
calculan, nunca se escriben). El negocio pregunta siempre por las conclusiones.

Comprobado: `user:juan` / `can_edit` / `project:alpha` → **PERMITIDO**, sin que exista
ninguna tupla que haga a Juan editor. Llega por `team:backend#member`. En la traza se ve el
momento clave: el motor encuentra una tupla cuyo sujeto es un conjunto, no puede concluir, y
**vuelve a preguntar** — esa recursión es Zanzibar entero.

**2. `from parent`.** Con una única tupla (`organization:acme#admin@user:maria`) María puede
editar 8 objetos: los 3 proyectos de Acme, sus carpetas y sus recursos. Dos variantes que
conviene no confundir:

- `admin from parent` (en `project`): sube y pregunta **otra** relación arriba. No recursa.
- `can_edit from parent` (en `folder`): sube y pregunta **la misma** relación, que volverá a
  subir. Eso convierte una regla en una escalera, y como `define parent: [project, folder]`
  admite el propio tipo, funciona igual con 3 niveles que con 50.

**3. El modelo es un dato.** La misma pregunta (`maria` / `can_edit` / `resource:b`) da
PERMITIDO con el modelo completo y DENEGADO con el modelo sin herencia en carpetas, **sin
tocar una sola tupla**. El permiso no vive en los datos ni en el modelo, vive en el cruce de
los dos. Consecuencia práctica: cambiar una política es publicar un modelo, no migrar datos —
y puedes evaluar contra el modelo nuevo antes de activarlo.

---

## Lección 4 — Intersección: el caso que hace explotar RBAC

Sofía es colaboradora externa. Tiene exactamente una tupla: `project:alpha#editor@user:sofia`.
**No** es miembro de Acme.

La regla, la única del modelo con `and`:

```
define can_publish: can_edit and member from parent
```

Modela una frase de negocio muy común: *para publicar hay que poder editar **y** ser de la casa*.

**Pregunta 4.1 — ¿Qué puede hacer Sofía sobre `project:alpha`?**

- (a) Editar sí, publicar no
- (b) Editar y publicar
- (c) Ni editar ni publicar
- (d) Publicar sí, editar no

**Pregunta 4.2 —** Misma pregunta (`can_publish` sobre `project:alpha`) para **Juan**, que
tampoco aparece nombrado en Acme por ninguna tupla directa. ¿Mismo resultado que Sofía o distinto?

```bash
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:sofia","relation":"can_edit","object":"project:alpha"}'
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:sofia","relation":"can_publish","object":"project:alpha","explain":true}'
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:juan","relation":"can_publish","object":"project:alpha","explain":true}'
```

<details><summary>Respuesta</summary>

**(a) Editar sí, publicar no.** Son los casos guiados **O** y **P** del escenario.

`can_edit` es ALLOW por su tupla de editora. `can_publish` exige las dos ramas del `and`:
`can_edit` (la cumple) **y** `member from parent`, que sube a `organization:acme` y pregunta
`member` allí. Sofía no es miembro, así que la intersección falla.

Juan sí publica, y por la misma tupla que ya conoces: `organization:acme#member@team:backend#member`
le hace miembro de Acme por pertenecer al equipo. **Cumple las dos ramas por vías distintas** —
edita por `editor` vía equipo, y es miembro de la organización vía el mismo equipo.

Lo que esto le hace a RBAC: la condición "editor pero externo" no es un rol, es una
*combinación*. En RBAC se resuelve inventando `editor`, `editor-que-publica`,
`editor-externo`… y la combinatoria de condiciones se convierte en catálogo de roles. Es la
*role explosion* de la lección 11.

En la traza, fíjate en que el nodo raíz es una intersección en lugar de una unión, y que
basta con que **un** hijo falle para que todo falle — al revés que la unión.
</details>

---

## Lección 5 — Varios caminos, y por qué revocar es difícil

Juan tiene dos tuplas que le acercan a Alpha:

```
project:alpha#viewer@user:juan                  ← directa
project:alpha#editor@team:backend#member        ← vía equipo, y can_view incluye can_edit
```

**Pregunta 5.1 —** Preguntas `user:juan` / `can_view` / `project:alpha` con `"explain": true`.
¿Cuántos caminos devuelve el campo `paths`?

**Pregunta 5.2 — La importante.** Sacas a Juan del equipo backend (borras
`team:backend#member@user:juan`). ¿Pierde el acceso de lectura a Alpha?

**Pregunta 5.3 —** ¿Y por qué el mismo check **sin** `explain` es más barato en el campo
`metrics.storeQueries`?

```bash
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:juan","relation":"can_view","object":"project:alpha","explain":true}'
```

Para 5.2, borra y vuelve a poner la tupla. Ojo: el `DELETE` recibe la tupla por **query
string**, no por body, y el `#` hay que escaparlo como `%23`:

```bash
curl -s -X DELETE "http://localhost:15101/access-control/relationships?tuple=team:backend%23member@user:juan"
# ... vuelve a preguntar ...
curl -s -X POST "http://localhost:15101/access-control/relationships" -H "Content-Type: application/json" \
  -d '{"tuple":"team:backend#member@user:juan"}'
```

(La respuesta del borrado trae además un `diff` con los casos guiados que cambian de
resultado. Merece la pena mirarlo.)

Si algo se descuadra: `curl -X POST http://localhost:15101/access-control/seed/reset` devuelve
el escenario al estado inicial.

<details><summary>Respuesta</summary>

**5.1 — Dos caminos.** Es el caso guiado **H**, y el escenario lo declara con
`MinimumPaths: 2` para que el test de conformidad falle si alguna vez se pierde uno.

**5.2 — No pierde el acceso.** Le queda el `viewer` directo: comprobado, sigue en ALLOW y
`paths` pasa de 2 a 1. Y aquí está la lección
incómoda de ReBAC: **"¿por qué tiene acceso?" puede tener varias respuestas a la vez**, así
que quitar la que se te ocurrió primero no revoca nada. En un sistema de roles la pregunta
suele tener una sola respuesta; aquí hay que ver todos los caminos antes de creerte que has
revocado algo. Por eso el explorador devuelve `paths` en plural y no "el motivo".

**5.3 —** Sin `explain` el motor **corta en cuanto una vía concede** (lo verás anotado en la
traza: *primera vía que concede acceso, el resto no se evalúa*). Con `explain` recorre todas
las ramas porque necesita enseñarte también lo que **no** funcionó. En producción pagas lo
primero; en el explorador, lo segundo. Es la razón de que `Explain` sea un flag del
`CheckRequest` y no el comportamiento por defecto.

Medido en la lección 1 con `can_edit`: 2 consultas y 3 tuplas sin explain, 5 y 5 con explain.
</details>

---

## Lección 6 — Exclusión: `but not`, y por qué es local

Ana es de Globex. La cadena que la acerca a `resource:d`:

```
organization:globex#member@team:sales#member
team:sales#member@user:ana
project:delta#viewer@organization:globex#member
resource:d#parent@project:delta
resource:d#blocked@user:ana          ← la excepción
```

El modelo, con la relación intermedia:

```
define viewable: viewer or shared_with or can_edit or can_view from parent
define can_view: viewable but not blocked
```

**Pregunta 6.1 —** `user:ana` / `can_view` / `resource:d` → ¿permitido o denegado?

**Pregunta 6.2 —** `user:ana` / `can_view` / `project:delta` (el **padre** del recurso
bloqueado) → ¿permitido o denegado?

**Pregunta 6.3 —** ¿Por qué `viewable` existe como relación intermedia, en lugar de escribir
directamente `can_view: viewer or shared_with or ... but not blocked`?

```bash
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:ana","relation":"can_view","object":"resource:d","explain":true}'
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:ana","relation":"can_view","object":"project:delta"}'
```

<details><summary>Respuesta</summary>

**6.1 — Denegado** (caso **M**). Ana *sí* tendría acceso por la cadena de Globex; la tupla
`blocked` anula **todos** los caminos a la vez. La exclusión no compite con las vías que
conceden: gana siempre.

**6.2 — Permitido** (caso **N**). El bloqueo estaba en el recurso, no en el proyecto: **las
exclusiones no se propagan hacia arriba**. Poner los dos casos juntos es lo que enseña que
`but not` es una regla *de ese objeto y esa relación*, no un estado global de la persona.

**6.3 —** Porque mezclar `or` con `but not` en la misma línea obligaría a paréntesis para ser
inequívoco, y el modelo del laboratorio se escribe **sin paréntesis** para que sea válido tal
cual en OpenFGA. La salida es la que OpenFGA obliga a tomar: una relación intermedia que
agrupa la unión, y luego una línea limpia que le resta la exclusión. De paso, `viewable` deja
la intención más clara que un paréntesis: *"todas las vías por las que se podría ver"*.

Y un coste oculto que merece la pena tener presente ya: esta línea es la que **rompe la
expansión inversa** de ListObjects (lección 9). No se puede saber a quién hay que quitar sin
comprobarlo objeto a objeto.
</details>

---

## Lección 7 — Grupos anidados y comodines

Dos mecanismos que parecen especiales y no lo son.

```
group:seguridad#member@user:pedro
group:auditoria#member@group:seguridad#member     ← un grupo dentro de otro
resource:e#editor@group:auditoria#member
project:gamma#viewer@user:*                        ← comodín
```

**Pregunta 7.1 —** `user:pedro` / `can_edit` / `resource:e`. ¿Permitido? Y si lo es, ¿cuánto
código específico para "grupos anidados" crees que hay en `CheckEvaluator`?

**Pregunta 7.2 —** `user:ana` / `can_view` / `project:gamma`. Ana es de Globex y Gamma es de
Acme, no comparten ninguna organización. ¿Permitido?

**Pregunta 7.3 —** ¿Cuál es la diferencia real entre `team` y `group` en el modelo, y por qué
existen los dos?

```bash
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:pedro","relation":"can_edit","object":"resource:e","explain":true}'
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:ana","relation":"can_view","object":"project:gamma","explain":true}'
```

<details><summary>Respuesta</summary>

**7.1 — Permitido** (caso **J**), y **cero código específico**. `define member: [user, group#member]`
admite el propio tipo como sujeto, exactamente igual que `parent: [project, folder]` en las
carpetas. Es la misma recursión que resuelve todo lo demás; la anidación de grupos sale
gratis del modelo, no del motor. Si te sorprende, mira la traza: los nodos son los mismos
`tuple` → `relation` → `_this` de la lección 1.

**7.2 — Permitido** (caso **K**). La tupla `@user:*` concede a cualquier usuario. Es la forma
de modelar "público" sin escribir una tupla por persona — que es justo lo que tendría que
hacer un sistema de roles, y lo que hace que "publicar algo" sea O(usuarios) en vez de O(1).

**7.3 —** `team` tiene `parent: [organization]`; `group` no tiene padre. Un grupo puede
cruzar organizaciones (el grupo Auditoría mezcla gente de sitios distintos), un equipo no.
La diferencia no es cosmética: enseña que **el modelo no impone una única forma de agrupar**,
cada tipo declara sus propias reglas. Y aun así los dos se consumen igual desde
`project.editor`, porque `[user, team#member, group#member]` acepta ambos usersets sin que el
motor necesite saber cuál vino.
</details>

---

## Lección 8 — `shared_with`: cuando la relación es el registro de la intención

Ana no tiene absolutamente nada en Acme, salvo esto:

```
resource:a#shared_with@user:ana
```

Funcionalmente `shared_with` podría haber sido `viewer` — conceden lo mismo, las dos entran
en `viewable`.

**Pregunta 8.1 —** Entonces, ¿por qué están separadas en el modelo? ¿Qué pregunta puedes
responder con dos relaciones que no puedes responder con una?

**Pregunta 8.2 —** Borras la tupla `shared_with`. ¿Cuánto tarda Ana en perder el acceso, y
qué hay que invalidar?

```bash
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:ana","relation":"can_view","object":"resource:a","explain":true}'
curl -s "http://localhost:15101/access-control/audit?subject=user:ana"
```

<details><summary>Respuesta</summary>

**8.1 —** Puedes responder **"¿esto lo ve por su rol, o porque alguien se lo compartió?"**,
que es exactamente la pregunta de una revisión de accesos. Con una sola relación las dos
situaciones son indistinguibles a posteriori. La relación es el **registro de la intención**,
no solo el mecanismo del permiso: te dice *por qué* se concedió, no solo *que* se concedió.

Es un patrón que se generaliza: cuando dos vías conceden lo mismo pero significan cosas
distintas para un humano, sepáralas en el modelo y únelas en la conclusión (`viewable`).
Cuesta una línea y te ahorra un campo de auditoría paralelo que habría que mantener
sincronizado a mano.

**8.2 —** Lo pierde **en la siguiente pregunta**, y no hay nada que invalidar: no existe
ninguna caché de permisos ni ninguna tabla materializada. Es el caso guiado **F** y es la
contrapartida honesta de la lección 5: los caminos múltiples hacen difícil revocar *del todo*,
pero borrar una tupla concreta surte efecto inmediato sin propagación.

(La memoización de la lección 10 es **por petición**, no entre peticiones — por eso no
contradice esto.)
</details>

---

## Lección 9 — ListObjects: la pregunta cara

Hasta ahora siempre has preguntado *"¿puede X sobre este objeto concreto?"*. La pantalla de
un producto necesita la otra: *"¿qué proyectos puede ver X?"*. Es la misma información y un
problema computacional completamente distinto.

Hay dos estrategias implementadas: `reverse` (expansión inversa desde el sujeto) y la
ingenua (comprobar objeto por objeto).

**Pregunta 9.1 —** ¿Por qué no se puede resolver siempre con expansión inversa? Pista: la
respuesta está en la lección 6.

**Pregunta 9.2 —** Ejecuta `list-objects/compare` para `user:maria` sobre `resource`. ¿Cuál
de las dos estrategias lee más tuplas, y cuánto más?

**Pregunta 9.3 —** Compara el `reason` que devuelve la expansión inversa para un `project`
con el de un `resource`. ¿Por qué en uno dice que la pertenencia es concluyente y en el otro
hacen falta *comprobaciones de confirmación*?

```bash
curl -s -X POST http://localhost:15101/access-control/list-objects -H "Content-Type: application/json" \
  -d '{"subject":"user:maria","relation":"can_edit","objectType":"resource"}'
curl -s -X POST http://localhost:15101/access-control/list-objects/compare -H "Content-Type: application/json" \
  -d '{"subject":"user:maria","relation":"can_edit","objectType":"resource"}'
```

Lectura de apoyo: `docs/04-list-objects-y-rendimiento.md`.

<details><summary>Respuesta</summary>

**9.1 —** Por `can_view: viewable but not blocked`. La expansión inversa parte del sujeto y
*acumula* los objetos alcanzables; pero una regla no monótona (una exclusión) puede **quitar**
un objeto del conjunto, y no hay forma de saber cuáles sin comprobarlo uno a uno. Por eso el
motor, cuando la relación tiene exclusiones, hace una pasada de **confirmación** sobre los
candidatos: la inversa propone, el `Check` dispone.

**9.2 — La inversa, y por bastante.** Medido sobre el escenario inicial:

| `user:maria` / `can_edit` / `resource` | Tuplas leídas | Consultas | Confirmaciones |
|---|---|---|---|
| Ingenua | 20 | 34 | — |
| Expansión inversa | **16** | **23** | 0 |

`can_edit` sobre `resource` es `editor or owner or can_edit from parent`: todo uniones y
herencia, es decir reglas **monótonas**. La inversa lo resuelve del tirón, sin confirmar nada.

**9.3 — Y aquí viene lo que casi nunca se cuenta: no siempre gana.** La misma comparación con
`can_view`, que arrastra el `but not blocked`:

| `user:maria` / `can_view` / `resource` | Tuplas leídas | Consultas | Confirmaciones |
|---|---|---|---|
| Ingenua | **27** | **53** | — |
| Expansión inversa | 46 | 78 | **4** |

La inversa **pierde**. La exclusión no se puede invertir, así que la expansión solo produce
*candidatos* y hay que confirmar cada uno con un `Check` real: acaba haciendo prácticamente los
mismos checks que la ingenua, más el coste de la expansión.

Ahí está la respuesta a la 9.3: el `reason` de un `project` dice que la pertenencia al conjunto
es concluyente porque todas sus reglas son monótonas; el de un `resource` habla de
comprobaciones de confirmación porque `can_view` termina en una exclusión.

**La conclusión que importa:** la ventaja de la expansión inversa no viene del algoritmo en
abstracto, viene de la **proporción entre el tamaño del catálogo y el del conjunto accesible**.
Con cinco recursos y cuatro visibles no hay nada que ahorrar. Con doscientos mil documentos y
doce visibles, es la diferencia entre una pantalla que carga y una que no — eso lo demuestra el
test `ListObjects_WhenTheCatalogIsMuchBiggerThanWhatTheSubjectCanSee_ReverseExpansionWins`,
que añade 200 proyectos invisibles y mide una mejora de más de 10×.

El test `ListObjects_ReverseExpansion_AgreesWithTheNaiveOracle` es el que sostiene todo esto:
compara las dos estrategias exhaustivamente. Si falla, el bug está en la inversa — la ingenua
es lenta pero obviamente correcta, y por eso sirve de oráculo.
</details>

---

## Lección 10 — Las tres salvaguardas del motor

`CheckEvaluator.cs` es el fichero más importante del repositorio, y todo lo que hace más allá
de evaluar el modelo son tres protecciones: **profundidad, ciclos y memoización**.

Provoca tú un ciclo. Los grupos admiten `group#member` como sujeto, así que:

```bash
# A es miembro de B y B es miembro de A
curl -s -X POST http://localhost:15101/access-control/relationships -H "Content-Type: application/json" \
  -d '{"tuple":"group:ciclo-a#member@group:ciclo-b#member"}'
curl -s -X POST http://localhost:15101/access-control/relationships -H "Content-Type: application/json" \
  -d '{"tuple":"group:ciclo-b#member@group:ciclo-a#member"}'
curl -s -X POST http://localhost:15101/access-control/check -H "Content-Type: application/json" \
  -d '{"subject":"user:ana","relation":"member","object":"group:ciclo-a","explain":true}'
```

**Pregunta 10.1 —** ¿Qué pasa: se cuelga, desborda la pila, o responde? Mira
`metrics.cyclesDetected`.

**Pregunta 10.2 — La sutil.** El motor memoiza resultados dentro de una misma petición. Pero
hay dos `false` que tiene **prohibido** memoizar. ¿Cuáles, y por qué?

**Pregunta 10.3 —** ¿Por qué la memoización es por petición y no un caché entre peticiones?
¿Qué se rompería?

Deshaz el ciclo cuando termines: `curl -X POST http://localhost:15101/access-control/seed/reset`.

<details><summary>Respuesta</summary>

**10.1 — Responde.** El motor lleva la pila de nodos en curso; al reencontrar el mismo par
(relación, objeto) marca el nodo como ciclo, devuelve `false` **para esa rama** y sigue. En
`metrics.cyclesDetected` verás la cuenta. Lo que hace un sistema que no lo contempla es
desbordar la pila con datos que un usuario puede escribir — es decir, una denegación de
servicio escribiendo dos tuplas.

**10.2 — El `false` de un ciclo y el `false` del límite de profundidad.** Ninguno de los dos
es una conclusión sobre el problema: son *"no lo sé por aquí"*, no *"no"*. Si los memoizaras,
una rama que se cortó por ciclo envenenaría otra rama distinta que sí habría llegado a la
respuesta, y el resultado dependería del orden de evaluación. Está escrito tal cual en el
código: *"No se memoiza: este 'false' no es una conclusión sobre el problema"*. Es la regla
que `CLAUDE.md` marca como no negociable.

**10.3 —** Porque entre peticiones el store puede haber cambiado, y un permiso cacheado es un
permiso revocado que sigue funcionando. Dentro de una misma petición el conjunto de tuplas es
estable, así que memoizar es seguro y evita reevaluar el mismo subárbol en un grafo con
rombos (varias vías que confluyen en el mismo nodo). El caching *entre* peticiones existe en
Zanzibar de verdad, pero va acompañado de zookies y consistencia; eso es `docs/06`.
</details>

---

## Lección 11 — ReBAC contiene a RBAC

El argumento central del laboratorio. El tercer modelo publicado, `RbacEquivalent`, imita RBAC
**dentro** del motor ReBAC en cinco líneas:

```
type role
  relations
    define assignee: [user]

type project
  relations
    define can_view: [user, role#assignee]
```

`assignee` es la tabla `user_roles`. La lista de sujetos de `can_view` es `role_permissions`.

El módulo de negocio tiene además un motor RBAC de verdad, con sus tablas, sembrado con el
mismo escenario: **5 roles, 47 permisos, 7 asignaciones = 59 filas** para expresar lo que en
ReBAC son 42 tuplas.

**Pregunta 11.1 —** Si RBAC se puede expresar en ReBAC, ¿qué es exactamente lo que **no** se
puede hacer al revés? Busca una frase del modelo completo que sea inexpresable en un esquema
usuario→rol→permiso.

**Pregunta 11.2 —** Ejecuta la comparación para Sofía y para María y mira dónde discrepan los
dos motores. ¿Por qué discrepan?

**Pregunta 11.3 —** Añades el proyecto número 501 a Acme. ¿Cuántas filas hay que escribir para
que María (admin) pueda editarlo, en ReBAC y en RBAC?

```bash
curl -s -X POST http://localhost:15100/comparison/check -H "Content-Type: application/json" \
  -d '{"userId":"sofia","action":"edit","objectRef":"project:alpha"}'
curl -s -X POST http://localhost:15100/comparison/check -H "Content-Type: application/json" \
  -d '{"userId":"maria","action":"edit","objectRef":"resource:b"}'
```

Lectura de apoyo: `docs/05-rebac-vs-rbac-vs-abac.md`.

<details><summary>Respuesta</summary>

**11.1 —** Cualquier frase donde el permiso dependa del **contexto del objeto**. El ejemplo
canónico del repositorio: *"los miembros del equipo que es editor del proyecto padre de esta
carpeta"*. En RBAC el permiso no tiene sujeto ni contexto — es un par (rol, permiso) sin
lugar donde colgar "de esta carpeta". La única salida es multiplicar roles
(`editor-proyecto-alpha`, `editor-proyecto-beta`, …) hasta que el catálogo es inmanejable.
Eso es la *role explosion*.

**11.2 —** Discrepan justo donde hay herencia y donde hay intersección: RBAC solo sabe lo que
alguien materializó en una fila, así que o bien falta el permiso, o bien alguien tuvo que
propagarlo a mano (y entonces queda desactualizado en cuanto algo se mueve). ReBAC lo deriva
al preguntar.

**11.3 — Una en ReBAC** (`project:nuevo#parent@organization:acme`, que además tendrías que
escribir de todas formas) **y cero adicionales para María**: `admin from parent` la alcanza
sola. En RBAC hay que crear los roles del proyecto nuevo y asignarlos a todo el que deba
tenerlos — y no olvidarte de nadie. Ese "y no olvidarte de nadie" repetido 500 veces es el
verdadero coste.
</details>

---

## Ideas para después del tour

Cosas que el laboratorio permite y que no entran en las preguntas de arriba:

- **Publicar un modelo tuyo** (`POST /access-control/models`) y responder la misma pregunta
  con los cuatro modelos a la vez, para ver el diff de decisiones afectadas.
- **`/expand`**: en lugar de preguntar por una persona, despliega el árbol completo de quién
  tiene una relación sobre un objeto. Es la vista contraria a `check`.
- **`/graph`**: el grafo de tuplas, que es lo que dibuja el frontend.
- **`/act-as`** en el módulo de negocio: entrar como cada persona del escenario y ver el
  producto tal y como lo ve ella. Es la comprobación de que el negocio nunca conoce el modelo.
- **Romper cosas a propósito**: borra `organization:acme#member@team:backend#member` y mira
  cuántos casos guiados se caen de golpe. Es la mejor demostración de lo que carga una sola
  tupla de userset.

---

## Apéndice — `tour.py`

Formatea la traza como árbol. Guárdalo donde quieras y ejecútalo con
`python tour.py user:juan can_edit project:alpha`. En Windows necesita
`PYTHONIOENCODING=utf-8` para las tildes y las flechas.

```python
import io, sys, json, subprocess

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")
BASE = "http://localhost:15101/access-control"

def post(path, body):
    r = subprocess.run(["curl", "-s", "-X", "POST", BASE + path,
        "-H", "Content-Type: application/json", "-d", json.dumps(body)],
        capture_output=True, text=True, encoding="utf-8")
    return json.loads(r.stdout)

def tree(n, pre="", last=True, root=True):
    mark = "OK " if n["allowed"] else "NO "
    conn = "" if root else ("`- " if last else "|- ")
    detail = f"   ({n['detail']})" if n.get("detail") else ""
    print(f"{pre}{conn}{mark}[{n['kind']}] {n['label']}{detail}")
    kids = n.get("children") or []
    npre = pre + ("" if root else ("   " if last else "|  "))
    for i, k in enumerate(kids):
        tree(k, npre, i == len(kids) - 1, False)

def check(subject, relation, obj, explain=True, **kw):
    body = {"subject": subject, "relation": relation, "object": obj, "explain": explain}
    body.update(kw)
    r = post("/check", body)
    print(f"?  {subject}  --{relation}-->  {obj}")
    print(f"=> {'PERMITIDO' if r['allowed'] else 'DENEGADO'}\n")
    print("TRAZA:")
    tree(r["trace"])
    if r.get("paths"):
        print("\nCAMINOS:")
        for p in r["paths"]:
            print(f"  [{p['length']} saltos] {p['chain']}")
    m = r["metrics"]
    print(f"\nCOSTE: {m['storeQueries']} consultas | {m['tuplesRead']} tuplas leidas | "
          f"{m['nodesEvaluated']} nodos | prof.max {m['maxDepthReached']} | "
          f"memo {m['memoizationHits']} | ciclos {m['cyclesDetected']} | {m['durationMs']}ms")
    return r

if __name__ == "__main__":
    check(sys.argv[1], sys.argv[2], sys.argv[3])
```
