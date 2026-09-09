# 3. Cómo funciona un `Check`

El código está en
[`CheckEvaluator.cs`](../backend/access-control/application/Authorization/Engine/CheckEvaluator.cs).
Son unas doscientas líneas y contienen, literalmente, el algoritmo de autorización de Zanzibar.

## La pregunta siempre es la misma

```
¿Pertenece SUJETO al conjunto de quienes tienen RELACIÓN sobre OBJETO?
```

Y se responde **de arriba abajo**: se mira qué dice el modelo sobre esa relación y, según la
regla que encuentre, el problema se transforma en *otros problemas de la misma forma*, hasta
llegar a tuplas concretas.

No hay ningún caso especial para equipos, para grupos ni para jerarquías. Los tres salen de
aplicar las mismas cinco reglas.

| Regla | Cómo transforma el problema |
|---|---|
| `_this` | **Único caso base.** Lee tuplas. Si alguna apunta a un userset, el problema pasa a ser "¿pertenece el sujeto a *ese* conjunto?" |
| `computed_userset` | Cambia la RELACIÓN, mismo objeto |
| `tuple_to_userset` | Cambia el OBJETO (sube al padre) y la RELACIÓN. **Aquí está la herencia** |
| `union` / `intersection` / `exclusion` | Combinan resultados de subproblemas |

## Un ejemplo completo

`Check(user:juan, can_edit, resource:a)`. Fíjate en que **no existe** ninguna tupla
`resource:a#can_edit@user:juan`:

```
Check(user:juan, can_edit, resource:a)
│  modelo: can_edit: editor or owner or can_edit from parent
│
├─ [1] editor              → lee resource:a#editor@?  → 0 tuplas         ✕
│
├─ [2] owner               → lee resource:a#owner@?   → 0 tuplas         ✕
│
└─ [3] can_edit from parent                          ← tuple_to_userset
      │  lee resource:a#parent@? → project:alpha
      │
      └─ Check(user:juan, can_edit, project:alpha)   ← RECURSIÓN
            │  modelo: can_edit: editor or owner or admin from parent
            │
            └─ editor  → lee project:alpha#editor@?
                  │      → project:alpha#editor@team:backend#member
                  │
                  │  el sujeto de la tupla es un USERSET, no una persona
                  └─ Check(user:juan, member, team:backend)  ← RECURSIÓN
                        │
                        └─ lee team:backend#member@? → @user:juan   ✓
                                                                ⇒ ALLOW
```

Tres niveles de recursión, cinco consultas al almacén, y el mismo código en cada nivel.

## Por qué se va del objeto hacia el sujeto

No es arbitrario, y es la clave del rendimiento.

Partiendo del **objeto**, cada paso está acotado por las tuplas de *ese* objeto, que son pocas:
un proyecto tiene un puñado de editores, no un millón. Partiendo del **sujeto** habría que
explorar todo lo que el sujeto toca, que puede ser medio sistema.

Es exactamente la razón por la que `Check` es rápido y `ListObjects` —que sí va del sujeto
hacia los objetos— es el problema difícil. Ver [doc 04](./04-list-objects-y-rendimiento.md).

## Las tres salvaguardas

Un motor de autorización sin estas tres cosas es un motor que tumba el servicio.

### 1. Límite de profundidad

Zanzibar usa ~25. **No es una optimización, es una necesidad de disponibilidad**: sin él, una
jerarquía muy profunda o un modelo mal escrito convierten un `Check` en una consulta ilimitada.
Y como el `Check` está en el camino crítico de cada petición, eso es una caída total.

Un matiz importante: cuando se alcanza el límite, la respuesta correcta es *"no se pudo
determinar"*, no *"no tiene acceso"*. En el laboratorio se puede bajar a 3 y ver cómo un
recurso enterrado deja de ser accesible.

### 2. Detección de ciclos

Grupos que se contienen mutuamente. **No es un caso rebuscado**: aparece en cuanto el modelo
admite grupos anidados y alguien mete el grupo A en el B y el B en el A — una operación de
administración perfectamente normal.

Se lleva un conjunto de nodos "en visita". Si se vuelve a entrar en el mismo, esa rama devuelve
DENY y se abandona.

Detalle sutil, y fácil de equivocar: ese `false` **no se memoiza**. No es una conclusión sobre
el problema, solo significa "por aquí no se llega a nada nuevo". Otra rama puede resolver
legítimamente el mismo subproblema.

### 3. Memoización por petición

El mismo subproblema puede aparecer en varias ramas. La caché **nace al empezar el Check y se
tira al terminar**, así que no puede quedar obsoleta: no hay ninguna ventana en la que una
tupla borrada siga concediendo acceso.

Es una caché gratis en términos de correctitud. La que sí es peligrosa —la que sobrevive entre
peticiones, la que Zanzibar necesita de verdad— se trata en el
[doc 06](./06-caching-consistencia-concurrencia.md).

## Cortocircuito: rapidez contra explicación

En una `union`, la primera rama que concede termina la evaluación. Es lo que hace rápido el
`Check` de producción, y también la razón por la que **ese Check no puede contarte cuántos
caminos hay**: en cuanto uno concede, los demás no se miran.

Por eso el motor tiene dos modos:

| | `CheckOptions.Default` | `CheckOptions.Explain` |
|---|---|---|
| Cortocircuito | Sí | No |
| Caminos encontrados | Máximo 1 | Todos (hasta 20) |
| Ramas fallidas en la traza | Sí | Sí |
| Sugerencias en un DENY | No | Sí |
| Quién lo usa | El negocio | El Authorization Explorer |

En el laboratorio se puede comparar el número de consultas de uno y otro sobre la misma
pregunta. Explicar cuesta.

## Los múltiples caminos, y por qué importan

Juan puede ver Project Alpha por dos vías: una tupla `viewer` directa, y `editor` a través de
su equipo (porque `can_view` incluye `can_edit`).

Esto tiene dos caras:

- **Buena:** el acceso legítimo es resistente. Reorganizar un equipo no rompe accesos que
  vienen por otro lado.
- **Mala, y peligrosa:** *sacar a Juan del equipo no le quita el acceso*. Si no ves todos los
  caminos, crees que has revocado y no has revocado nada.

Es el fallo de seguridad característico de ReBAC, y el motivo por el que el Explorer muestra
todos los caminos en lugar de uno.

## Por qué la traza vale más que el booleano

El `Check` de OpenFGA devuelve `{allowed: true}` y nada más. Aquí devuelve el recorrido entero,
**incluidas las ramas que fallaron**, y con ese único objeto se alimentan tres cosas sin
escribir la lógica tres veces:

- el árbol indentado del Explorer,
- el resaltado del grafo,
- el camino que se guarda en la auditoría.

Ese es el motivo principal por el que este proyecto tiene motor propio en lugar de llamar a
OpenFGA: sin la traza, los puntos 7, 8, 10-H y 13 del enunciado no tendrían datos que mostrar.

> En un sistema real se devolvería el booleano y la traza solo bajo una bandera de depuración:
> serializar el árbol en cada petición es caro.

## El DENY como material didáctico

Un DENY explicado enumera **todas** las vías por las que ese objeto puede conceder ese permiso,
y propone las tuplas que faltan:

```
❌ DENY — user:ana no tiene can_edit sobre project:alpha

Bastaría con UNA de estas relaciones:
  project:alpha#editor@user:ana              ← nombrarla directamente
  project:alpha#editor@team:sales#member     ← un conjunto al que YA pertenece
  project:alpha#owner@user:ana
  organization:acme#admin@user:ana           ← conceder en el padre, para que herede
```

La segunda es la sugerencia interesante: **no hace falta tocar a la persona**, basta con dar
acceso a un grupo del que ya forma parte.

> **Aviso para producción:** esto no se expone al usuario final. Decirle a alguien qué relación
> le falta filtra la estructura interna de la organización — "no eres administrador de Acme" ya
> confirma que Acme existe y que tiene administradores. En producción, endpoint solo para
> administradores.

---

Anterior: [2. El modelo](./02-modelo-de-autorizacion.md) ·
Siguiente: [4. ListObjects y rendimiento](./04-list-objects-y-rendimiento.md)
