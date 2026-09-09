# 6. Caching, consistencia y concurrencia

Este documento trata la parte de Zanzibar que **este laboratorio no implementa** a propósito, y
por qué. Es también la parte del paper que más gente se salta y la que más problemas da en
producción.

## Por qué aparecen estos problemas

Con el motor de autorización **dentro** del proceso, ninguno de estos problemas existe:

- La latencia es cero, así que no hace falta cachear.
- Sin caché, no hay invalidación.
- Escribes una tupla y preguntas: es la misma transacción, así que la consistencia es trivial.

En cuanto el control de acceso es un **servicio aparte** —que es la situación real de
cualquiera que use Zanzibar, OpenFGA o SpiceDB— los tres aparecen de golpe.

Por eso el laboratorio tiene el interruptor `Authorization:Mode`. Con `InProcess`, estos tres
capítulos son teoría que lees aquí. Con `Remote`, los mides en pantalla.

## Caching: qué se puede cachear y qué no

La asimetría del [doc 02](./02-modelo-de-autorizacion.md) manda:

| | Se cachea | Por qué |
|---|---|---|
| **El modelo** | Sí, agresivamente y sin caducidad | Es **inmutable**. El id es un hash del DSL, así que dos modelos con el mismo id tienen el mismo contenido. Una entrada no puede quedar obsoleta |
| **Las tuplas** | Aquí, no | Cambian continuamente y una entrada obsoleta significa **conceder acceso que ya se revocó** |
| **El resultado de un Check** | Aquí, no | Lo mismo, y peor: es el dato que más directamente se traduce en un fallo de seguridad |

Ver
[`PostgresAuthorizationModelStore`](../backend/access-control/infrastructure/Repositories/PostgresAuthorizationModelStore.cs):
la caché de modelos es un `static` sin caducidad, y es correcto precisamente porque el dato que
cachea no puede cambiar.

### La única caché segura que sí usamos

La **memoización por petición** del `EvaluationContext`: nace al empezar el `Check` y se tira al
terminar. No existe ninguna ventana en la que una tupla borrada siga concediendo acceso. Es
gratis en términos de correctitud.

### Por qué invalidar una caché de autorización es tan difícil

Supongamos que cacheamos el resultado de `Check(user:juan, can_edit, resource:a)`. Ahora alguien
borra la tupla `team:backend#member@user:juan`.

**¿Qué entradas de caché hay que invalidar?**

No se sabe sin recorrer el grafo entero hacia adelante. Esa tupla afecta a:

- todo lo que `team:backend` alcance directamente,
- todo lo que cuelgue de esos objetos, a cualquier profundidad,
- todo lo que dependa de `organization:acme#member` (porque backend es miembro de Acme),
- y todo lo que cuelgue de *eso*.

En el laboratorio se puede ver el alcance: el endpoint de crear relación re-evalúa los 16 casos
guiados antes y después y te dice cuáles cambiaron. **Una sola tupla puede cambiar decisiones
sobre objetos que no tienen nada que ver a simple vista.**

Ese cálculo es viable con 16 casos y 42 tuplas. Con billones de tuplas es imposible, y es
justamente el motivo de que Zanzibar **no invalide**: usa otro enfoque.

## Consistencia: el problema de los zookies

### El problema, con un ejemplo

```
1. Juan quita a Pedro del equipo backend.        (escritura)
2. Juan recarga la lista de proyectos.           (lectura)
3. Pedro sigue apareciendo con acceso.           😱
```

¿Bug? Depende. Si el sistema de autorización tiene réplicas y la lectura fue a una que aún no
había recibido la escritura, es el comportamiento esperado de un sistema distribuido. Y es
inaceptable de cara al usuario, que acaba de hacer una acción de seguridad y ve que no ha
surtido efecto.

### La solución del paper: zookies

Zanzibar devuelve, en cada escritura de tupla, un **zookie**: un token opaco que codifica el
instante lógico de esa escritura.

```
escribir tupla  →  zookie "ABC123"
Check(..., consistency: at_least_as_fresh("ABC123"))
```

El `Check` con ese zookie garantiza una respuesta **al menos tan reciente** como esa escritura.
Si la réplica que atiende no está suficientemente al día, espera o redirige.

Lo que hace el zookie es dejar que **quien llama** elija el punto del compromiso:

| Modo | Latencia | Garantía | Cuándo usarlo |
|---|---|---|---|
| Sin zookie (cualquier réplica) | Mínima | Puede estar desfasado unos segundos | Listar contenido, pintar una vista |
| Con zookie | Mayor | Al menos tan reciente como la escritura | Justo después de revocar un acceso |
| Consistencia total | Máxima | Siempre al día | Operaciones críticas |

Y esa es la idea que merece la pena llevarse aunque nunca implementes zookies: **la consistencia
de la autorización es un parámetro por operación, no una propiedad global del sistema.**

### Qué hace este laboratorio

**Nada de esto.** Una sola instancia, una sola base de datos, sin réplicas y sin caché entre
peticiones. Cada `Check` lee el estado actual, siempre.

Es una decisión consciente: preferimos que cada evaluación sea observable y reproducible antes
que rápida. Añadir replicación aquí solo añadiría una fuente de confusión sin enseñar nada que
no se pueda leer en este documento.

## Concurrencia

### La escritura de tuplas es idempotente

`WriteAsync` no duplica: si la tupla existe, devuelve la existente. Importa porque "invitar a
alguien que ya estaba invitado" pasa constantemente, y dos hechos idénticos harían que la traza
mostrara el mismo camino dos veces, como si hubiera dos motivos independientes de acceso.

El índice único de la tabla lo garantiza a nivel de base de datos, con un detalle de PostgreSQL
que es fácil pasar por alto: **en un índice único, varios `NULL` se consideran distintos entre
sí**. Como `subject_relation` es `NULL` para los individuos, sin `NULLS NOT DISTINCT`
(PostgreSQL 15+) se podrían insertar dos filas idénticas para `@user:juan`. Ver
[`RelationshipTupleConfiguration`](../backend/access-control/infrastructure/Persistence/Configurations/RelationshipTupleConfiguration.cs).

### La lectura durante un Check no es atómica

Un `Check` hace varias consultas. Entre la primera y la última, otra transacción puede escribir
o borrar tuplas. El resultado puede reflejar un estado que **nunca existió** como instantánea
consistente.

En la práctica se acepta, y no por dejadez: envolver cada `Check` en una transacción
serializable multiplicaría el coste de la operación más frecuente del sistema para evitar una
condición de carrera de milisegundos que casi siempre es irrelevante. Zanzibar resuelve esto
con instantáneas a un instante lógico concreto (Spanner), que es una infraestructura que aquí no
tenemos.

### La duplicación de la jerarquía

Un problema práctico que este laboratorio deja **deliberadamente a la vista**.

La organización de un equipo existe dos veces:

- `teams.organization_id` en la base de negocio,
- `team:backend#parent@organization:acme` en la base de control de acceso.

Están en **bases de datos distintas**, así que no hay transacción que las cubra. Pueden
desincronizarse, y cuando lo hacen el síntoma es horrible de diagnosticar: la interfaz muestra
que el equipo pertenece a Acme y la autorización se comporta como si no.

Las salidas habituales, todas con pegas:

1. **El control de acceso como fuente única.** El negocio consulta la jerarquía al módulo. Lo
   acopla al servicio que debía gobernarlo.
2. **Escritura transaccional con outbox.** Correcto y bastante trabajo.
3. **Reconciliación periódica.** Un job que compara y corrige. Es lo que hace casi todo el
   mundo.

El laboratorio hace la 3ª versión más simple: ambos salen del mismo
[`PlaygroundScenario`](../backend/access-control/application/Authorization/Seed/PlaygroundScenario.cs),
así que arrancan alineados. Y hay una deuda visible a propósito en
`DeleteProjectCommandHandler`: **al borrar un proyecto, sus tuplas se quedan**. Bórralo y mira
el grafo — las tuplas huérfanas siguen ahí, concediendo acceso a algo que ya no existe.

## Modo degradado: qué hacer si el módulo no responde

Solo se plantea en modo `Remote`, y la respuesta correcta es **denegar**.

Ver
[`RemoteAccessControlService`](../backend/business/infrastructure/Authorization/RemoteAccessControlService.cs):
está implementado explícitamente y con su explicación, no como efecto secundario de un `catch`
vacío. Permitir cuando no se puede comprobar convierte una caída del servicio de autorización en
acceso libre a todo el sistema.

La contrapartida es dura y hay que asumirla: **si el control de acceso cae, cae todo**. Es
exactamente la razón por la que Zanzibar está diseñado con una disponibilidad tan alta y por la
que Google le dedicó un paper entero.

Hay una asimetría más en ese adaptador que merece la pena notar: las **lecturas** fallidas
degradan a DENY, pero las **escrituras** fallidas propagan el error. Si al crear un proyecto no
se logra declarar quién es su dueño, el proyecto queda huérfano y nadie podrá tocarlo nunca.
Mejor que la operación entera falle.

---

Anterior: [5. ReBAC vs RBAC vs ABAC](./05-rebac-vs-rbac-vs-abac.md) ·
Volver al [README](../README.md)
