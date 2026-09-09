# 5. ReBAC vs RBAC vs ABAC

Los tres modelos responden a preguntas distintas. La comparación útil no es cuál es "mejor",
sino **qué pregunta sabe responder cada uno** y **cuánto cuesta mantenerlo**.

| | Pregunta que responde | Dato que guarda |
|---|---|---|
| **RBAC** | ¿Qué **rol** tiene esta persona? | usuario → rol → permiso |
| **ReBAC** | ¿Qué **relación** hay entre esta persona y esta cosa? | tuplas `objeto#relación@sujeto` |
| **ABAC** | ¿Qué **atributos** tienen la persona, el recurso y el contexto? | políticas sobre atributos |

## Lo que RBAC hace mejor

Conviene empezar por aquí, porque es fácil salir de un laboratorio de ReBAC pensando que RBAC
es un modelo roto. No lo es.

- **El motor es diez veces más simple.** Compara
  [`RbacEngine`](../backend/business/infrastructure/Rbac/RbacEngine.cs) (dos consultas y una
  comparación de cadenas) con
  [`CheckEvaluator`](../backend/access-control/application/Authorization/Engine/CheckEvaluator.cs).
  Sin recursión, sin ciclos, sin profundidad máxima, sin expansión inversa.
- **El razonamiento siempre tiene la misma longitud.** Tres pasos, sin importar lo complicada
  que sea la organización. Se audita fácil y se explica a alguien no técnico en una frase.
- **Revocar es trivial y fiable.** Quitas el rol y se acabó. En ReBAC hay que comprobar que no
  quede otro camino — y ese es su fallo de seguridad característico.
- **`ListObjects` es un `JOIN`.** Sin punto fijo, sin cierre transitivo, sin índice Leopard.
- **No necesita infraestructura.** Son tres tablas en tu base de datos, no un servicio aparte
  con su propia base y su propia disponibilidad.

**Si tu aplicación tiene tres roles, no hay jerarquía, no se comparte nada y los objetos no se
mueven de sitio, RBAC es la respuesta correcta** y montar Zanzibar sería un error caro.

## Dónde se rompe RBAC

El problema de RBAC no está en el motor: está en **mantener las filas**.

Como un permiso nombra un objeto concreto (`project:alpha:edit`) y no sabe expresar relaciones
entre objetos, hay que **enumerar**. Y reenumerar cada vez que algo cambia:

| Evento | RBAC | ReBAC |
|---|---|---|
| Se crea un recurso en Alpha | Añadir filas a cada rol que deba alcanzarlo | 1 tupla (`parent`) |
| Se mueve una carpeta | Quitar filas de unos roles, ponerlas en otros, **y las de todo lo que colgaba** | 1 tupla reescrita |
| Alguien entra en el equipo | Asignarle los roles correctos | 1 tupla (`member`) |
| Se crea un proyecto | **Crear roles nuevos** (los permisos nombran objetos) | 1 tupla (`parent`) |
| Dar acceso a 5.000 personas | 5.000 filas | 1 tupla a un conjunto |

El [`RbacSeeder`](../backend/business/infrastructure/Rbac/RbacSeeder.cs) tiene ese bucle de
enumeración escrito a propósito, para que se vea el producto cartesiano.

Y la señal de alarma en un sistema real no es el número de usuarios: es **cuándo empiezas a
ver roles cuyo nombre contiene el identificador de un objeto concreto** (`editor-proyecto-alpha`).
Eso es RBAC pidiendo a gritos ser ReBAC.

## Los tres casos donde RBAC no llega

En la pantalla **RBAC vs ReBAC** del laboratorio hay preajustes para los tres:

1. **Acceso a través de un grupo** (Pedro → Seguridad → Resource A). RBAC puede hacerlo *si
   alguien materializó la fila*. Cuando no, se manifiesta como "a este usuario se le olvidó
   darle permiso".
2. **Herencia jerárquica** (Pedro → carpeta → subcarpeta → Resource B). RBAC necesita propagar
   al conceder, y volver a propagar al mover.
3. **Condiciones compuestas** (Sofía puede editar pero no publicar). RBAC lo resuelve
   inventando un rol más. Cada condición nueva multiplica el catálogo.

Y el caso inverso, que también aparece en el laboratorio: **RBAC concede y ReBAC no**. Suele
ser un permiso materializado que se quedó obsoleto — el usuario salió del equipo hace dos años
y la fila sigue ahí. Es el fallo de seguridad típico de RBAC: nadie recuerda limpiar.

## ReBAC contiene a RBAC

Un rol es simplemente un objeto intermedio. Cinco líneas de modelo:

```
type role
  relations
    define assignee: [user]

type project
  relations
    define can_edit: [user, role#assignee]
```

`role#assignee` es `user_roles`. La lista de `can_edit` es `role_permissions`. Está publicado
en el laboratorio como el modelo **«RBAC expresado en ReBAC»**.

**Lo contrario no se puede hacer.** No hay forma de expresar "los miembros del equipo que es
editor del proyecto padre de esta carpeta" en un esquema usuario→rol→permiso, porque en RBAC el
permiso no tiene sujeto ni contexto.

## Cuándo ABAC es mejor

ABAC decide con **atributos** de la persona, del recurso y del contexto, evaluados con reglas:

```
permitir si  usuario.departamento == recurso.departamento
        AND  hora ENTRE 09:00 Y 18:00
        AND  peticion.ip EN red_corporativa
        AND  recurso.clasificacion <= usuario.nivel_habilitacion
```

**ReBAC no puede expresar nada de esto**, y no es una carencia de implementación: no son
relaciones entre entidades, son propiedades evaluadas en el momento.

ABAC es mejor cuando la decisión depende de:

- **El contexto de la petición**: hora, IP, dispositivo, país, nivel de confianza de la sesión.
- **Valores del recurso**: importe, clasificación, estado, antigüedad. *"Puede aprobar gastos
  de hasta 10.000 €"* es ABAC puro — el límite es un número, no una relación.
- **Cálculos**: *"solo durante los 30 días posteriores a la creación"*.
- **Reglas que cambian a menudo sin que cambien los datos.**

Las pegas de ABAC, que son reales:

- **`ListObjects` es prácticamente imposible.** Para saber qué puede ver alguien hay que
  evaluar la política contra todos los recursos. No hay índice inverso posible sobre reglas
  arbitrarias.
- **Explicar una decisión es difícil.** Una política con quince condiciones no produce un
  "camino" legible.
- **Es difícil de auditar.** *"¿Quién puede ver esto?"* no tiene respuesta sin evaluar la
  política contra toda la población de usuarios.

### En la práctica, se combinan

Casi todos los sistemas serios acaban con **ReBAC para la estructura y ABAC para el contexto**:

```
ReBAC:  ¿tiene Juan alguna relación que le conceda can_edit sobre este documento?
  ↓ (si sí)
ABAC:   ¿además está en horario laboral, desde la red corporativa,
        y el documento no está clasificado por encima de su nivel?
```

OpenFGA lo soporta con *conditional relationship tuples* (tuplas con condiciones sobre
parámetros de contexto), y es exactamente esta combinación. Este laboratorio **no las
implementa** a propósito: mezclar los dos modelos antes de entender bien uno los confunde.

## Guía rápida de elección

| Situación | Modelo |
|---|---|
| Pocos roles fijos, sin jerarquía, sin compartir | **RBAC** |
| Los usuarios comparten cosas entre sí | **ReBAC** |
| Hay jerarquía (carpetas, proyectos, organizaciones) | **ReBAC** |
| Equipos y grupos que dan acceso | **ReBAC** |
| Multi-tenant con estructura interna por tenant | **ReBAC** |
| La decisión depende de la hora, la IP o el importe | **ABAC** |
| Necesitas "qué puede ver este usuario" rápido | **ReBAC** (ABAC no puede) |
| Roles con el nombre de un objeto concreto | **ReBAC** (RBAC ya se rompió) |
| Estructura compleja **y** condiciones de contexto | **ReBAC + ABAC** |

---

Anterior: [4. ListObjects y rendimiento](./04-list-objects-y-rendimiento.md) ·
Siguiente: [6. Caching, consistencia y concurrencia](./06-caching-consistencia-concurrencia.md)
