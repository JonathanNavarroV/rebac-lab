# 1. Qué problema resuelve ReBAC, y qué es Zanzibar

## El problema, contado desde el sitio donde duele

Casi todos los sistemas de permisos empiezan igual y bien:

```
usuario → rol → permiso
```

Juan tiene el rol `editor`, el rol `editor` tiene el permiso `project:edit`, luego Juan puede
editar proyectos. Esto es **RBAC**, funciona, se audita fácil y se le explica a cualquiera en
una frase. No hay ninguna necesidad de complicarlo mientras la pregunta sea *"¿qué puede hacer
esta persona?"*.

El problema aparece cuando la pregunta cambia a *"¿qué puede hacer esta persona **sobre esta
cosa concreta**?"*. Y esa pregunta aparece siempre, tarde o temprano, porque los usuarios la
hacen sin darse cuenta:

- *"Juan puede editar proyectos… pero solo los suyos."*
- *"…y también los de su equipo."*
- *"…y los de la organización si es administrador."*
- *"…y los que le hayan compartido."*
- *"Ah, y si puede ver una carpeta, debería ver lo que hay dentro."*

RBAC solo tiene una herramienta para responder a esto: **crear más roles**.

```
editor-proyecto-alpha
editor-proyecto-beta
editor-proyecto-gamma
viewer-carpeta-docs
viewer-carpeta-docs-interno
editor-externo-que-no-publica
...
```

Esto se llama **role explosion**, y no es un problema teórico: es la razón por la que en muchas
empresas hay un catálogo de tres mil roles que nadie entiende, que nadie se atreve a limpiar, y
en el que la respuesta a *"¿por qué esta persona tiene acceso a esto?"* es *"porque en 2021
alguien le dio el rol `editor-proyecto-alpha` y ya nadie sabe si sigue haciendo falta"*.

## El cambio de perspectiva

ReBAC (*Relationship-Based Access Control*) le da la vuelta a la pregunta. En lugar de:

> ¿Qué **permisos** tiene esta persona?

pregunta:

> ¿Qué **relación** hay entre esta persona y esta cosa?

Y la relación no tiene por qué ser directa. Puede ser una cadena:

```
Juan  --member-->  Team Backend  --editor-->  Project Alpha  --parent-->  Resource A
```

Ninguna de esas cuatro flechas dice "Juan puede editar Resource A". Esa conclusión **se deduce
al preguntar**, recorriendo el grafo. Y ahí está todo el cambio: en RBAC el permiso es un dato
que alguien escribió; en ReBAC es una **conclusión que se calcula**.

Las consecuencias prácticas de esa diferencia son grandes:

| | RBAC | ReBAC |
|---|---|---|
| Meter a alguien en un equipo | Hay que asignarle los roles correspondientes | Una tupla, y hereda todo automáticamente |
| Mover un recurso de carpeta | Recalcular y reescribir sus permisos | Reescribir **una** tupla |
| Crear un recurso en un proyecto | Añadir filas a cada rol que deba alcanzarlo | **Una** tupla: de qué cuelga |
| Dar acceso a 5.000 personas | 5.000 filas | Una tupla a un conjunto |
| "¿Por qué tiene acceso?" | Por el rol X | Por este camino concreto de N saltos |
| Quitar el acceso | Quitar el rol | Cuidado: puede haber **varios** caminos |

Esa última fila también es un aviso. ReBAC no es gratis, y su dificultad característica es
justo la contraria de la de RBAC: **es fácil dar acceso sin darse cuenta del alcance, y es
fácil creer que has revocado algo cuando no**. Por eso el laboratorio insiste tanto en mostrar
*todos* los caminos.

## Qué es Zanzibar

En 2019 Google publicó *[Zanzibar: Google's Consistent, Global Authorization
System](https://research.google/pubs/pub48190/)*, describiendo el sistema que decide quién
puede ver qué en Drive, YouTube, Calendar, Photos y Cloud. Las cifras del paper dan idea del
problema: **billones de tuplas**, **millones de consultas por segundo**, y un **p95 por debajo
de 10 ms**.

No liberaron el código, solo el diseño. De ahí salieron varias reimplementaciones:

| Implementación | Origen | Nota |
|---|---|---|
| **OpenFGA** | Auth0/Okta, donado a la CNCF | La más adoptada. Su DSL es el que usa este proyecto |
| **SpiceDB** | AuthZed | La más fiel al paper, incluye *zookies* |
| **Ory Keto** | Ory | Más simple, menos features |

### ReBAC y Zanzibar no son lo mismo

Es una confusión habitual y conviene separarlo:

- **ReBAC** es el **modelo conceptual**: el acceso se deriva de relaciones entre entidades. Es
  una idea, y es anterior a Google.
- **Zanzibar** es una **arquitectura concreta** para implementar ReBAC a escala planetaria:
  las tuplas, las reglas de reescritura de usersets, las tres operaciones (`Check`, `Expand`,
  `Read`), el índice Leopard, los zookies, la replicación global.

En este proyecto:

| Parte | Qué es |
|---|---|
| El modelo de relaciones y su semántica | **ReBAC** |
| Las tuplas `objeto#relación@sujeto` | **Zanzibar** (formato del paper) |
| Las cinco reglas de reescritura | **Zanzibar** (sección 2.3 del paper) |
| `Check` / `Expand` / `ListObjects` | **Zanzibar** (su API) |
| Que el módulo sea un servicio aparte | **Zanzibar** (su arquitectura) |
| Zookies, Leopard, replicación global | **Zanzibar**, y aquí **no** están (ver [doc 06](./06-caching-consistencia-concurrencia.md)) |

## Qué NO resuelve ReBAC

Para no salir de aquí pensando que es la respuesta a todo:

- **No resuelve la autenticación.** Quién eres es otro problema.
- **No sabe de atributos.** *"Solo en horario laboral"*, *"solo desde la red de la oficina"*,
  *"solo si el importe es menor de 10.000 €"* no son relaciones. Eso es **ABAC**, y se trata en
  el [doc 05](./05-rebac-vs-rbac-vs-abac.md).
- **No sustituye a la validación de negocio.** Que puedas editar un pedido no significa que el
  pedido pueda editarse: puede estar cerrado, facturado o cancelado. Son cosas distintas y
  conviene no mezclarlas.
- **No es más simple.** El motor de este proyecto tiene diez veces más código que el motor RBAC
  equivalente. Lo que ahorra es el trabajo de **mantener las filas**, no el de escribir el
  motor.

## Cuándo NO usarlo

Si tu aplicación tiene tres roles, no hay jerarquía, no se comparte nada y los objetos no se
mueven de sitio, **RBAC es la respuesta correcta** y montar Zanzibar sería un error caro. La
señal de que te hace falta algo más no es el número de usuarios: es cuándo empiezas a ver roles
cuyo nombre contiene el identificador de un objeto concreto.

---

Siguiente: [2. El modelo de autorización, relación por relación](./02-modelo-de-autorizacion.md)
