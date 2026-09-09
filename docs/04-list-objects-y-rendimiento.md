# 4. `ListObjects`: el problema difícil

`Check` responde *"¿puede Juan ver **este** proyecto?"*. `ListObjects` responde *"¿**qué**
proyectos puede ver Juan?"*.

Parece la misma pregunta del revés. No lo es, y la diferencia es toda la asimetría del
[doc 03](./03-como-funciona-check.md): `Check` va del objeto hacia el sujeto, acotado por las
tuplas de ese objeto. `ListObjects` no tiene objeto del que partir.

## Estrategia A — Naive: enumerar y comprobar

```
1. SELECT id FROM projects              → N candidatos
2. para cada uno: Check(juan, can_view, project:X)
```

Trivialmente correcta. Y trivialmente inviable: **O(N) checks sobre el tamaño del catálogo**,
no sobre el tamaño de la respuesta.

Con 20 proyectos, 20 checks e imperceptible. Con 200.000 documentos, 200.000 checks — y si el
módulo de control de acceso es remoto, 200.000 viajes de red. La pantalla no carga.

Es el error que comete casi todo el mundo la primera vez, y conviene haberlo medido una vez.

En el laboratorio se conserva por dos motivos:

1. **Es el oráculo.** La suite de conformidad comprueba que la expansión inversa devuelve
   exactamente el mismo conjunto. Si difieren, el bug está en la inversa.
2. **Es la única que sabe qué NO ves.** Al mirar todos los objetos, puede explicar por qué cada
   uno quedó fuera. La expansión inversa no puede — y no es un defecto de implementación, es
   que nunca llega a mirarlos.

## Estrategia B — Expansión inversa

Lo que hace de verdad el `ListObjects` de OpenFGA. Tres fases, y las tres son necesarias.

### Fase 1 — ¿A qué conjuntos pertenece el sujeto?

Antes de mirar ningún objeto hay que saber quién es Juan *a efectos de autorización*: no solo
`user:juan`, sino también `team:backend#member`, `organization:acme#member`,
`group:seguridad#member`… y los conjuntos a los que pertenecen *esos* conjuntos,
transitivamente.

Es un **cierre transitivo de pertenencia**, y en Google es tan caro que le dedicaron un sistema
entero: el índice **Leopard** del paper existe exactamente para esto.

Se acota con dos ideas:

- Solo importan las relaciones que el modelo usa **como userset** (típicamente `member`). Si
  nadie escribe nunca `algo#viewer` como sujeto, no hace falta buscar de qué es viewer Juan.
- El comodín (`user:*`) es un alias más: hay que buscarlo igual que se busca al sujeto.

### Fase 2 — Propagación monótona hasta punto fijo

Con los alias en mano, **una consulta al índice inverso** da los objetos donde el sujeto tiene
una relación directa. Eso es la semilla. Falta propagar:

- **Hacia los permisos derivados:** si tiene `editor`, entonces `can_edit`, y por tanto
  `can_view`. Sin tocar la base de datos.
- **Hacia abajo por la jerarquía:** si puede ver la carpeta raíz, puede ver todo lo de dentro.
  Aquí sí hay consulta, y es la clave: se pregunta *"¿quién tiene como padre a **este**
  objeto?"*, no se recorre nada más.

Se hace con una **lista de trabajo, no con recursión**, y no es una decisión de estilo: el
modelo tiene reglas recursivas (`folder.parent` admite `folder`), así que una recursión directa
no terminaría. Lo que se necesita es un **punto fijo**: ir añadiendo objetos hasta que una
pasada completa no añada ninguno nuevo.

### Fase 3 — Confirmación de lo que no se puede invertir

**Esta es la parte que casi nunca se cuenta.**

La fase 2 solo funciona con reglas **monótonas**: añadir tuplas solo puede añadir accesos. La
unión, la relación computada y la herencia lo son.

La **exclusión** (`but not blocked`) y la **intersección** (`can_edit and member from parent`)
no. Con ellas, añadir una tupla puede *quitar* acceso, y no hay forma de saber a quién hay que
restar sin mirar objeto por objeto.

La solución honesta, y la que usan las implementaciones reales: tratar la fase 2 como
generadora de un **superconjunto de candidatos** y confirmar cada uno con un `Check` real.

Sigue siendo mucho mejor que la naive —los candidatos son "lo que el sujeto podría ver", no
"todo el catálogo"— pero deja de ser gratis.

## Los números del laboratorio

Con el dataset inicial, `user:juan` / `can_view` / `project`:

| | Naive | Inversa |
|---|---|---|
| Resultado | alpha, beta, gamma | alpha, beta, gamma |
| Consultas al almacén | 13 | **7** |
| Sabe qué NO ves | Sí (delta, con motivo) | No |

Y con `resource` en lugar de `project` (donde el modelo tiene el `but not blocked`):

| | Naive | Inversa |
|---|---|---|
| Consultas al almacén | 72 | **90** |

**La inversa sale más cara.** No es un bug: es la fase 3 pagando el precio de la exclusión,
sobre un catálogo de cinco recursos donde Juan ve cuatro. Cuando el sujeto ve casi todo, no hay
nada que ahorrar.

La ventaja de la expansión inversa **no viene del algoritmo en abstracto**: viene de la
proporción entre el tamaño del catálogo y el del conjunto accesible. El test
`ListObjects_WhenTheCatalogIsMuchBiggerThanWhatTheSubjectCanSee_ReverseExpansionWins` lo
demuestra añadiendo 200 proyectos que Juan no puede ver: ahí la inversa gana por más de 10×.

Es exactamente la situación real: 200.000 documentos y tú puedes ver doce.

## Las trampas que quedan

### La paginación no puede ser un `LIMIT` ingenuo

En la naive, `LIMIT 20` sobre el catálogo devuelve 20 candidatos de los que quizá solo 3 pasen
el check. En la inversa, el conjunto se construye por punto fijo y no está ordenado de forma
estable hasta que termina. Paginar `ListObjects` correctamente es un problema en sí mismo, y
por eso las APIs reales devuelven un cursor opaco en lugar de un offset.

### El patrón correcto de integración

Ni el módulo debe enumerar el catálogo del negocio, ni el negocio debe consultar las tuplas.

**El negocio enumera, el control de acceso decide.** El handler
[`GetProjectsQuery`](../backend/business/application/Features/Projects/Queries/GetProjects/GetProjectsQuery.cs)
lo hace así: pide al módulo la lista de referencias autorizadas y la **cruza** con su propio
catálogo. Así el módulo nunca necesita saber qué es un proyecto, y el negocio nunca necesita
saber qué es una tupla.

### Los objetos huérfanos

El módulo solo sabe que existe `project:alpha` porque alguien escribió una tupla que lo
menciona. Un proyecto recién creado sin ninguna relación **no existe** para él — lo cual es
correcto, porque nadie puede verlo. Ver
[`IObjectCatalog`](../backend/access-control/domain/Abstractions/IObjectCatalog.cs).

## Por qué existe `BatchCheck`

Pintar una lista de 50 recursos con sus botones de editar y borrar son **100 checks**.

- En modo `InProcess`: 100 llamadas a memoria. Da igual.
- En modo `Remote`: 100 viajes de red. La pantalla tarda segundos.

Toda API de autorización real acaba teniendo un `BatchCheck`, y no ahorra CPU: ahorra *viajes*.
En el laboratorio se ve en el contador `checksPerformed` del espacio de trabajo — cambia
`Authorization:Mode` a `Remote` y vuelve a mirarlo.

---

Anterior: [3. Cómo funciona un Check](./03-como-funciona-check.md) ·
Siguiente: [5. ReBAC vs RBAC vs ABAC](./05-rebac-vs-rbac-vs-abac.md)
