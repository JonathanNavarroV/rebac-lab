namespace Playground.AccessControl.Application.Authorization.Seed;

/// <summary>Qué se ejecuta de verdad para demostrar la respuesta de una pregunta.</summary>
public enum TourProbeKind
{
    /// <summary>Un <c>Check</c> normal sobre un objeto concreto.</summary>
    Check,

    /// <summary>Un listado resuelto con las dos estrategias, para comparar su coste.</summary>
    ListObjectsComparison,
}

/// <summary>
/// Una comprobación real contra el motor que respalda una respuesta.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta es la pieza que hace que el cuestionario no pueda mentir.</b> La respuesta correcta
/// de una pregunta de predicción no es un texto que alguien escribió: es lo que el motor
/// contesta al ejecutar estas sondas, aquí y ahora, contra las tuplas que haya en ese momento.
/// </para>
/// <para>
/// Los campos <c>Expected*</c> son lo que el cuestionario <i>afirma</i> que va a pasar. Un test
/// de conformidad los ejecuta todos y falla si alguno no se cumple, así que si alguien cambia
/// el modelo y una lección deja de ser cierta, se entera por el test y no por un alumno
/// confundido.
/// </para>
/// </remarks>
/// <param name="Object">
/// Para <see cref="TourProbeKind.Check"/>, el objeto (<c>project:alpha</c>). Para
/// <see cref="TourProbeKind.ListObjectsComparison"/>, el <b>tipo</b> de objeto (<c>resource</c>).
/// </param>
/// <param name="ExpectedReverseReadsFewerTuples">
/// Solo para comparaciones de listado. Es la afirmación que la lección 9 hacía sin comprobar, y
/// que resulta ser cierta unas veces y falsa otras según haya exclusiones por medio.
/// </param>
public sealed record TourProbe(
    TourProbeKind Kind,
    string Label,
    string Subject,
    string Relation,
    string Object,
    bool? ExpectedAllowed = null,
    int? ExpectedMinimumPaths = null,
    bool? ExpectedReverseReadsFewerTuples = null);

/// <summary>Una opción de respuesta.</summary>
public sealed record TourOption(string Key, string Text);

/// <summary>
/// Una pregunta del cuestionario.
/// </summary>
/// <param name="Kind">
/// <c>prediction</c> si la respuesta se puede comprobar ejecutando el motor; <c>concept</c> si
/// es una pregunta de criterio (por qué el modelo está escrito así) que no se puede ejecutar.
/// Distinguirlas importa: solo las primeras se autoverifican.
/// </param>
public sealed record TourQuestion(
    string Code,
    string LessonCode,
    string Kind,
    string Statement,
    IReadOnlyList<TourOption> Options,
    string CorrectOptionKey,
    string Explanation,
    IReadOnlyList<TourProbe> Probes,
    string? Hint = null);

/// <summary>Una lección: un bloque de contexto y sus preguntas.</summary>
public sealed record TourLesson(
    string Code,
    string Title,
    string Intro,
    IReadOnlyList<string> Facts,
    string? ModelSnippet = null);

/// <summary>
/// El cuestionario guiado: la versión ejecutable de <c>docs/07-tour-guiado.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// La mecánica es la del documento: lees el contexto, <b>apuestas</b> por una respuesta, y solo
/// entonces el sistema ejecuta la pregunta contra el motor y te enseña qué pasa de verdad. La
/// diferencia con el markdown es que aquí la evidencia no está escrita: se calcula al
/// responder, con la traza y las métricas de esa ejecución concreta.
/// </para>
/// <para>
/// Eso tiene un efecto secundario que vale más que el cuestionario en sí: si has estado
/// trasteando con las relaciones, las respuestas cambian contigo. Borra
/// <c>team:backend#member@user:juan</c> y vuelve a la lección 4 — Juan deja de poder publicar,
/// y la explicación sigue siendo correcta porque explica el <i>mecanismo</i>, no el resultado.
/// </para>
/// </remarks>
public static class TourQuestions
{
    public static IReadOnlyList<TourLesson> Lessons =>
    [
        new("1", "La tupla y el Check",
            "Todo el sistema es una tabla de hechos con la forma «objeto#relación@sujeto». El "
            + "sujeto puede ser una persona (user:juan) o un CONJUNTO (team:backend#member), y "
            + "esa segunda forma —el userset— es lo que separa ReBAC de una tabla de permisos: "
            + "una sola fila cubre a quien esté hoy y mañana en el equipo.",
            [
                "La pregunta que se le hace al motor son tres cadenas: sujeto, relación, objeto.",
                "«owner», «editor», «member» y «parent» son HECHOS: se escriben como tupla.",
                "«can_view», «can_edit» y «can_delete» son CONCLUSIONES: se calculan, nunca se escriben.",
                "El negocio pregunta siempre por las conclusiones.",
            ]),

        new("2", "Herencia: «from parent»",
            "Con una sola tupla, un administrador de organización alcanza todos los proyectos "
            + "presentes y futuros. Hay dos variantes de herencia que conviene no confundir.",
            [
                "«admin from parent» (en project): sube y pregunta OTRA relación arriba. No recursa.",
                "«can_edit from parent» (en folder): sube y pregunta LA MISMA relación, que volverá a subir.",
                "Como «define parent: [project, folder]» admite el propio tipo, funciona igual con 3 niveles que con 50.",
            ],
            "define can_edit: editor or owner or admin from parent"),

        new("3", "El modelo es un dato",
            "La misma pregunta puede dar respuestas distintas con dos modelos distintos, sin "
            + "tocar una sola tupla. El permiso no vive en los datos ni en el modelo: vive en el "
            + "cruce de los dos.",
            [
                "Cambiar una política es publicar un modelo, no migrar datos.",
                "Puedes evaluar contra el modelo nuevo antes de activarlo.",
            ]),

        new("4", "Intersección: el caso que hace explotar RBAC",
            "Sofía es colaboradora externa. Tiene exactamente una tupla: "
            + "«project:alpha#editor@user:sofia». NO es miembro de Acme. La regla de publicar es "
            + "la única del modelo con «and», y modela una frase de negocio muy común: para "
            + "publicar hay que poder editar Y ser de la casa.",
            [
                "project:alpha#editor@user:sofia",
                "organization:acme#member@team:backend#member  ← esto alcanza a Juan, no a Sofía",
                "team:backend#member@user:juan",
            ],
            "define can_publish: can_edit and member from parent"),

        new("5", "Varios caminos, y por qué revocar es difícil",
            "Juan tiene dos tuplas que le acercan a Alpha por vías independientes. Esto es lo "
            + "que hace que «¿por qué tiene acceso?» pueda tener varias respuestas a la vez.",
            [
                "project:alpha#viewer@user:juan                ← directa",
                "project:alpha#editor@team:backend#member      ← vía equipo, y can_view incluye can_edit",
            ]),

        new("6", "Exclusión: «but not», y por qué es local",
            "Ana llega a resource:d por una cadena de cuatro tuplas desde Globex. Y hay una "
            + "quinta que lo cambia todo: una excepción escrita sobre ella en ese recurso "
            + "concreto.",
            [
                "organization:globex#member@team:sales#member",
                "team:sales#member@user:ana",
                "project:delta#viewer@organization:globex#member",
                "resource:d#parent@project:delta",
                "resource:d#blocked@user:ana                   ← la excepción",
            ],
            "define viewable: viewer or shared_with or can_edit or can_view from parent\ndefine can_view: viewable but not blocked"),

        new("7", "Grupos anidados y comodines",
            "Dos mecanismos que parecen especiales y no lo son: un grupo dentro de otro, y una "
            + "tupla que concede a todo el mundo.",
            [
                "group:seguridad#member@user:pedro",
                "group:auditoria#member@group:seguridad#member   ← un grupo dentro de otro",
                "resource:e#editor@group:auditoria#member",
                "project:gamma#viewer@user:*                     ← comodín",
            ],
            "define member: [user, group#member]"),

        new("8", "«shared_with»: la relación como registro de la intención",
            "Ana no tiene absolutamente nada en Acme, salvo una tupla de compartición. "
            + "Funcionalmente «shared_with» podría haber sido «viewer»: conceden lo mismo y las "
            + "dos entran en «viewable».",
            [
                "resource:a#shared_with@user:ana   ← su ÚNICA vía hacia cualquier cosa de Acme",
            ]),

        new("9", "ListObjects: la pregunta cara",
            "Hasta ahora siempre has preguntado «¿puede X sobre este objeto concreto?». La "
            + "pantalla de un producto necesita la otra: «¿qué puede ver X?». Es la misma "
            + "información y un problema computacional completamente distinto.",
            [
                "Ingenua: recorre el catálogo y hace un Check por objeto. O(objetos del tipo).",
                "Expansión inversa: parte del sujeto y recorre el índice al revés. O(lo alcanzable).",
                "Una regla no monótona (un «but not») obliga a confirmar los candidatos uno a uno.",
            ]),

        new("10", "Las tres salvaguardas del motor",
            "CheckEvaluator.cs es el fichero más importante del repositorio, y todo lo que hace "
            + "más allá de evaluar el modelo son tres protecciones: profundidad, ciclos y "
            + "memoización.",
            [
                "Los grupos admiten «group#member» como sujeto, así que un usuario puede escribir un ciclo.",
                "La memoización es POR PETICIÓN, no entre peticiones.",
            ]),

        new("11", "ReBAC contiene a RBAC",
            "El argumento central del laboratorio. El modelo «RbacEquivalent» imita RBAC dentro "
            + "del motor ReBAC en cinco líneas: un rol es un objeto y «assignee» es la tabla "
            + "user_roles. Lo contrario no se puede hacer.",
            [
                "El mismo escenario: 42 tuplas en ReBAC, 59 filas en RBAC (5 roles + 47 permisos + 7 asignaciones).",
            ],
            "type role\n  relations\n    define assignee: [user]\n\ntype project\n  relations\n    define can_view: [user, role#assignee]"),
    ];

    public static IReadOnlyList<TourQuestion> Questions =>
    [
        // ── Lección 4 ───────────────────────────────────────────────────────
        new("4.1", "4", "prediction",
            "¿Qué puede hacer Sofía sobre project:alpha?",
            [
                new("a", "Editar sí, publicar no"),
                new("b", "Editar y publicar"),
                new("c", "Ni editar ni publicar"),
                new("d", "Publicar sí, editar no"),
            ],
            "a",
            "«can_edit» es ALLOW por su tupla de editora. «can_publish» exige las dos ramas del «and»: "
            + "can_edit (la cumple) Y «member from parent», que sube a organization:acme y pregunta "
            + "«member» allí. Sofía no es miembro, así que la intersección falla.\n\n"
            + "Lo que esto le hace a RBAC: «editor pero externo» no es un rol, es una COMBINACIÓN. En "
            + "RBAC se resuelve inventando editor, editor-que-publica, editor-externo… y la "
            + "combinatoria de condiciones se convierte en catálogo de roles.\n\n"
            + "En la traza fíjate en que el nodo raíz es una intersección y no una unión: basta con que "
            + "UN hijo falle para que todo falle, justo al revés que la unión.",
            [
                new(TourProbeKind.Check, "¿Sofía puede editar Alpha?",
                    "user:sofia", "can_edit", "project:alpha", ExpectedAllowed: true),
                new(TourProbeKind.Check, "¿Sofía puede publicar Alpha?",
                    "user:sofia", "can_publish", "project:alpha", ExpectedAllowed: false),
            ],
            "Mira las dos ramas del «and» por separado."),

        new("4.2", "4", "prediction",
            "Misma pregunta (can_publish sobre project:alpha) para Juan, que TAMPOCO aparece "
            + "nombrado en Acme por ninguna tupla directa. ¿Mismo resultado que Sofía?",
            [
                new("a", "Sí, también le deniega: tampoco está nombrado en Acme"),
                new("b", "No, a Juan sí le deja publicar"),
                new("c", "Le deniega, pero por un motivo distinto"),
            ],
            "b",
            "Juan sí publica, y por una tupla que ya conoces: «organization:acme#member@team:backend#member» "
            + "le hace miembro de Acme por pertenecer al equipo. Cumple las dos ramas por vías distintas: "
            + "edita por «editor» vía equipo, y es miembro de la organización vía el mismo equipo.\n\n"
            + "Aquí está la trampa de la pregunta: «no aparece nombrado» no significa «no lo es». En ReBAC "
            + "la pertenencia se DEDUCE, y por eso buscar el nombre de alguien en las tuplas es una forma "
            + "poco fiable de auditar quién tiene acceso.",
            [
                new(TourProbeKind.Check, "¿Juan puede publicar Alpha?",
                    "user:juan", "can_publish", "project:alpha", ExpectedAllowed: true),
                new(TourProbeKind.Check, "¿Juan es miembro de Acme?",
                    "user:juan", "member", "organization:acme", ExpectedAllowed: true),
            ]),

        // ── Lección 5 ───────────────────────────────────────────────────────
        new("5.1", "5", "prediction",
            "Preguntas user:juan / can_view / project:alpha con explicación completa. ¿Cuántos "
            + "caminos devuelve?",
            [
                new("a", "Uno: el motor para en cuanto encuentra una vía"),
                new("b", "Dos"),
                new("c", "Tres o más"),
            ],
            "b",
            "Dos caminos: el «viewer» directo, y «editor» vía su equipo (porque can_view incluye "
            + "can_edit). Es el caso guiado H, y el escenario lo declara con MinimumPaths: 2 para que el "
            + "test de conformidad falle si alguna vez se pierde uno.\n\n"
            + "Ojo con la opción (a): es cierta SIN explicación. Con explicación el motor desactiva el "
            + "cortocircuito precisamente para poder enseñarte todas las vías.",
            [
                new(TourProbeKind.Check, "Todos los caminos de Juan hacia Alpha",
                    "user:juan", "can_view", "project:alpha", ExpectedAllowed: true, ExpectedMinimumPaths: 2),
            ]),

        new("5.2", "5", "prediction",
            "LA IMPORTANTE. Sacas a Juan del equipo backend. ¿Pierde el acceso de lectura a Alpha?",
            [
                new("a", "Sí: su acceso venía del equipo"),
                new("b", "No: le queda el viewer directo"),
                new("c", "Lo pierde, pero solo tras vaciar la caché"),
            ],
            "b",
            "No lo pierde. Le queda el «viewer» directo, y «paths» pasa de 2 a 1.\n\n"
            + "Aquí está la lección incómoda de ReBAC: «¿por qué tiene acceso?» puede tener varias "
            + "respuestas a la vez, así que quitar la que se te ocurrió primero NO revoca nada. En un "
            + "sistema de roles la pregunta suele tener una sola respuesta; aquí hay que ver todos los "
            + "caminos antes de creerte que has revocado algo. Por eso el explorador devuelve «paths» en "
            + "plural y no «el motivo».\n\n"
            + "La opción (c) es tentadora y es falsa: no hay ninguna caché entre peticiones. Un borrado "
            + "surte efecto en la siguiente pregunta.",
            [
                new(TourProbeKind.Check, "Estado actual: ¿puede Juan ver Alpha?",
                    "user:juan", "can_view", "project:alpha", ExpectedAllowed: true, ExpectedMinimumPaths: 2),
                new(TourProbeKind.Check, "La vía del equipo, por separado",
                    "user:juan", "can_edit", "project:alpha", ExpectedAllowed: true),
                new(TourProbeKind.Check, "La vía directa, por separado",
                    "user:juan", "viewer", "project:alpha", ExpectedAllowed: true),
            ],
            "Cuenta las vías independientes antes de responder. Puedes comprobarlo de verdad "
            + "borrando la tupla en la pantalla de Relaciones."),

        new("5.3", "5", "concept",
            "¿Por qué el mismo check SIN explicación es más barato en metrics.storeQueries?",
            [
                new("a", "Porque no serializa la traza en la respuesta"),
                new("b", "Porque corta en cuanto una vía concede y no evalúa el resto"),
                new("c", "Porque usa una caché que la explicación desactiva"),
            ],
            "b",
            "Sin explicación el motor CORTA en cuanto una vía concede — lo verás anotado en la propia "
            + "traza: «primera vía que concede acceso, el resto no se evalúa». Con explicación recorre "
            + "todas las ramas porque necesita enseñarte también lo que NO funcionó.\n\n"
            + "En producción pagas lo primero; en el explorador, lo segundo. Es la razón de que Explain "
            + "sea un flag de la petición y no el comportamiento por defecto.\n\n"
            + "La (a) es un efecto real pero menor: serializar es CPU, no consultas al almacén. La (c) es "
            + "falsa: la memoización por petición sigue activa en los dos modos.",
            [
                new(TourProbeKind.Check, "Con explicación (recorre todas las ramas)",
                    "user:juan", "can_view", "project:alpha", ExpectedAllowed: true),
            ]),

        // ── Lección 6 ───────────────────────────────────────────────────────
        new("6.1", "6", "prediction",
            "user:ana / can_view / resource:d. Ana llega por la cadena de Globex, pero hay una "
            + "tupla «blocked» sobre ella. ¿Permitido o denegado?",
            [
                new("a", "Permitido: tiene más vías que conceden que exclusiones"),
                new("b", "Denegado"),
                new("c", "Depende del orden en que el motor evalúe las ramas"),
            ],
            "b",
            "Denegado (caso guiado M). Ana SÍ tendría acceso por la cadena de Globex; la tupla «blocked» "
            + "anula TODOS los caminos a la vez.\n\n"
            + "La exclusión no compite con las vías que conceden: gana siempre, y por eso la (a) y la (c) "
            + "son falsas. No es una votación ni depende del orden — la rama sustraída se evalúa aparte y "
            + "manda sobre el resultado.",
            [
                new(TourProbeKind.Check, "¿Ana puede ver Resource D?",
                    "user:ana", "can_view", "resource:d", ExpectedAllowed: false),
                new(TourProbeKind.Check, "¿Ana está bloqueada ahí?",
                    "user:ana", "blocked", "resource:d", ExpectedAllowed: true),
            ]),

        new("6.2", "6", "prediction",
            "user:ana / can_view / project:delta, es decir el PADRE del recurso donde está "
            + "bloqueada. ¿Permitido o denegado?",
            [
                new("a", "Denegado: el bloqueo se propaga a toda la jerarquía"),
                new("b", "Permitido"),
                new("c", "Denegado solo si el proyecto también tiene una tupla blocked"),
            ],
            "b",
            "Permitido (caso guiado N). El bloqueo estaba en el recurso, no en el proyecto: LAS "
            + "EXCLUSIONES NO SE PROPAGAN HACIA ARRIBA.\n\n"
            + "Poner los casos 6.1 y 6.2 juntos es lo que enseña que «but not» es una regla DE ESE OBJETO "
            + "Y ESA RELACIÓN, no un estado global de la persona. Ana no está «bloqueada»: está bloqueada "
            + "en un recurso concreto.\n\n"
            + "La (c) describe bien lo que pasaría, pero la respuesta a la pregunta tal como está el "
            + "escenario ahora es simplemente que sí puede.",
            [
                new(TourProbeKind.Check, "¿Ana puede ver Project Delta?",
                    "user:ana", "can_view", "project:delta", ExpectedAllowed: true),
                new(TourProbeKind.Check, "Y el recurso que cuelga de él",
                    "user:ana", "can_view", "resource:d", ExpectedAllowed: false),
            ]),

        new("6.3", "6", "concept",
            "¿Por qué «viewable» existe como relación intermedia, en lugar de escribir "
            + "directamente «can_view: viewer or shared_with or … but not blocked»?",
            [
                new("a", "Por rendimiento: evita reevaluar la unión dos veces"),
                new("b", "Para no mezclar «or» con «but not» en la misma línea y mantener el modelo válido en OpenFGA"),
                new("c", "Porque el parser del laboratorio no soporta «but not»"),
            ],
            "b",
            "Mezclar «or» con «but not» en la misma línea obligaría a paréntesis para ser inequívoco, y "
            + "el modelo del laboratorio se escribe SIN paréntesis para que sea válido tal cual en "
            + "OpenFGA. La salida es la que OpenFGA obliga a tomar: una relación intermedia que agrupa la "
            + "unión, y luego una línea limpia que le resta la exclusión.\n\n"
            + "De paso, «viewable» deja la intención más clara que un paréntesis: «todas las vías por las "
            + "que se podría ver».\n\n"
            + "Y un coste oculto que conviene tener presente ya: esta línea es la que ROMPE la expansión "
            + "inversa de ListObjects (lección 9).",
            []),

        // ── Lección 7 ───────────────────────────────────────────────────────
        new("7.1", "7", "prediction",
            "user:pedro / can_edit / resource:e. Pedro está en Seguridad, Seguridad está dentro "
            + "de Auditoría, y Auditoría es editora del recurso. ¿Permitido?",
            [
                new("a", "Sí, y el motor tiene código específico para resolver grupos anidados"),
                new("b", "Sí, y no hay ni una línea de código específica para grupos anidados"),
                new("c", "No: la anidación de grupos requiere activarla en el modelo con una regla aparte"),
            ],
            "b",
            "Permitido (caso guiado J), y CERO código específico. «define member: [user, group#member]» "
            + "admite el propio tipo como sujeto, exactamente igual que «parent: [project, folder]» en las "
            + "carpetas.\n\n"
            + "Es la misma recursión que resuelve todo lo demás: la anidación de grupos sale gratis del "
            + "MODELO, no del motor. Si te sorprende, mira la traza: los nodos son los mismos "
            + "tuple → relation → _this de la lección 1.",
            [
                new(TourProbeKind.Check, "¿Pedro puede editar Resource E?",
                    "user:pedro", "can_edit", "resource:e", ExpectedAllowed: true),
                new(TourProbeKind.Check, "¿Pedro es miembro de Auditoría, sin que nadie le metiera?",
                    "user:pedro", "member", "group:auditoria", ExpectedAllowed: true),
            ]),

        new("7.2", "7", "prediction",
            "user:ana / can_view / project:gamma. Ana es de Globex y Gamma es de Acme: no "
            + "comparten ninguna organización. ¿Permitido?",
            [
                new("a", "No: no hay ninguna relación entre Ana y Acme"),
                new("b", "Sí"),
            ],
            "b",
            "Permitido (caso guiado K). La tupla «project:gamma#viewer@user:*» concede a cualquier "
            + "usuario.\n\n"
            + "Es la forma de modelar «público» sin escribir una tupla por persona — que es justo lo que "
            + "tendría que hacer un sistema de roles, y lo que hace que «publicar algo» sea O(usuarios) en "
            + "vez de O(1).\n\n"
            + "El razonamiento de la (a) es correcto salvo por un detalle: el comodín no necesita ninguna "
            + "relación con la organización, y por eso hay que mirar TODAS las tuplas del objeto antes de "
            + "concluir que alguien no tiene acceso.",
            [
                new(TourProbeKind.Check, "¿Ana puede ver Project Gamma?",
                    "user:ana", "can_view", "project:gamma", ExpectedAllowed: true),
                new(TourProbeKind.Check, "¿Y Project Beta, que no es público?",
                    "user:ana", "can_view", "project:beta", ExpectedAllowed: false),
            ]),

        new("7.3", "7", "concept",
            "¿Cuál es la diferencia real entre «team» y «group» en el modelo, y por qué existen "
            + "los dos?",
            [
                new("a", "Ninguna funcional: es solo vocabulario de negocio"),
                new("b", "«team» tiene parent hacia una organización y «group» no, así que un grupo puede cruzar organizaciones"),
                new("c", "«group» admite anidación y «team» no"),
            ],
            "b",
            "«team» tiene «parent: [organization]»; «group» no tiene padre. Un grupo puede cruzar "
            + "organizaciones (el grupo Auditoría mezcla gente de sitios distintos), un equipo no.\n\n"
            + "La diferencia no es cosmética: enseña que EL MODELO NO IMPONE UNA ÚNICA FORMA DE AGRUPAR, "
            + "cada tipo declara sus propias reglas. Y aun así los dos se consumen igual desde "
            + "«project.editor», porque «[user, team#member, group#member]» acepta ambos usersets sin que "
            + "el motor necesite saber cuál vino.\n\n"
            + "La (c) es falsa y es un buen despiste: «team» también admite «team#member», así que los "
            + "equipos anidan igual de bien.",
            []),

        // ── Lección 8 ───────────────────────────────────────────────────────
        new("8.1", "8", "concept",
            "«shared_with» y «viewer» conceden lo mismo. ¿Qué pregunta puedes responder teniendo "
            + "las dos que no podrías con una sola?",
            [
                new("a", "«¿Cuántas personas tienen acceso?»"),
                new("b", "«¿Esto lo ve por su rol, o porque alguien se lo compartió?»"),
                new("c", "«¿Desde cuándo tiene acceso?»"),
            ],
            "b",
            "Puedes responder «¿esto lo ve por su rol, o porque alguien se lo compartió?», que es "
            + "exactamente la pregunta de una revisión de accesos. Con una sola relación las dos "
            + "situaciones son indistinguibles a posteriori.\n\n"
            + "La relación es el REGISTRO DE LA INTENCIÓN, no solo el mecanismo del permiso: te dice POR "
            + "QUÉ se concedió, no solo QUE se concedió.\n\n"
            + "Es un patrón que se generaliza: cuando dos vías conceden lo mismo pero significan cosas "
            + "distintas para un humano, sepáralas en el modelo y únelas en la conclusión («viewable»). "
            + "Cuesta una línea y te ahorra un campo de auditoría paralelo que habría que mantener a mano.",
            [
                new(TourProbeKind.Check, "El único acceso de Ana en Acme",
                    "user:ana", "can_view", "resource:a", ExpectedAllowed: true),
                new(TourProbeKind.Check, "Y NO es «viewer»: es «shared_with»",
                    "user:ana", "viewer", "resource:a", ExpectedAllowed: false),
            ]),

        new("8.2", "8", "concept",
            "Borras la tupla «shared_with». ¿Cuánto tarda Ana en perder el acceso y qué hay que "
            + "invalidar?",
            [
                new("a", "En la siguiente pregunta, y no hay nada que invalidar"),
                new("b", "Cuando expire la caché de decisiones"),
                new("c", "Tras recalcular los permisos materializados del recurso"),
            ],
            "a",
            "Lo pierde en la siguiente pregunta, y no hay nada que invalidar: no existe ninguna caché de "
            + "permisos ni ninguna tabla materializada.\n\n"
            + "Es la contrapartida honesta de la lección 5: los caminos múltiples hacen difícil revocar "
            + "DEL TODO, pero borrar una tupla concreta surte efecto inmediato y sin propagación.\n\n"
            + "La memoización de la lección 10 es POR PETICIÓN, no entre peticiones, así que no contradice "
            + "esto. En un Zanzibar de verdad sí hay caché entre peticiones, y por eso hacen falta los "
            + "zookies (docs/06).",
            [
                new(TourProbeKind.Check, "Estado actual del acceso de Ana",
                    "user:ana", "can_view", "resource:a", ExpectedAllowed: true),
            ],
            "Piensa en dónde está almacenada la decisión. ¿Está almacenada en algún sitio?"),

        // ── Lección 9 ───────────────────────────────────────────────────────
        new("9.1", "9", "concept",
            "¿Por qué no se puede resolver siempre con expansión inversa? La pista está en la "
            + "lección 6.",
            [
                new("a", "Porque el índice inverso no cubre todas las relaciones"),
                new("b", "Porque una exclusión puede QUITAR objetos del conjunto, y eso no se puede recorrer hacia atrás"),
                new("c", "Porque las jerarquías profundas superan el límite de profundidad"),
            ],
            "b",
            "Por «can_view: viewable but not blocked». La expansión inversa parte del sujeto y ACUMULA "
            + "los objetos alcanzables; pero una regla no monótona (una exclusión) puede QUITAR un objeto "
            + "del conjunto, y no hay forma de saber cuáles sin comprobarlo uno a uno.\n\n"
            + "Por eso el motor, cuando la relación tiene exclusiones, hace una pasada de CONFIRMACIÓN "
            + "sobre los candidatos: la inversa propone y el Check dispone. Lo verás en el campo "
            + "«confirmationChecks» de las métricas.",
            []),

        new("9.2", "9", "prediction",
            "Comparas las dos estrategias para user:maria / can_edit / resource. ¿Cuál lee menos "
            + "tuplas?",
            [
                new("a", "La ingenua: hace una sola pasada por el catálogo"),
                new("b", "La expansión inversa"),
                new("c", "Leen exactamente las mismas: solo cambia el orden"),
            ],
            "b",
            "La expansión inversa, y por bastante. «can_edit» sobre resource es «editor or owner or "
            + "can_edit from parent»: todo uniones y herencia, es decir reglas MONÓTONAS. La inversa puede "
            + "resolverlo del tirón, sin confirmar nada.\n\n"
            + "La diferencia crece con el tamaño del almacén: la ingenua es O(objetos del tipo) y la "
            + "inversa es O(lo alcanzable desde el sujeto). Con un catálogo de cinco recursos apenas se "
            + "nota; con doscientos mil documentos y doce visibles, es la diferencia entre una pantalla "
            + "que carga y una que no.\n\n"
            + "Mira los números reales de la evidencia: son de esta ejecución, no de un texto escrito a "
            + "mano.",
            [
                new(TourProbeKind.ListObjectsComparison, "Las dos estrategias, sobre recursos que María puede editar",
                    "user:maria", "can_edit", "resource", ExpectedReverseReadsFewerTuples: true),
            ]),

        new("9.3", "9", "prediction",
            "Ahora la misma comparación pero con «can_view» sobre resource, que sí tiene "
            + "exclusión. ¿Sigue ganando la expansión inversa?",
            [
                new("a", "Sí: siempre gana, es un algoritmo mejor"),
                new("b", "No necesariamente: aquí puede salirle más cara que a la ingenua"),
                new("c", "Da error: la inversa no soporta exclusiones"),
            ],
            "b",
            "NO necesariamente, y esta es la lección que casi nunca se cuenta.\n\n"
            + "«can_view» termina en «viewable but not blocked». La exclusión no se puede invertir, así que "
            + "la fase de expansión solo produce CANDIDATOS y hay que confirmar cada uno con un Check "
            + "real. Sobre un catálogo pequeño donde el sujeto ve casi todo, eso hace prácticamente los "
            + "mismos checks que la ingenua MÁS el coste de la expansión.\n\n"
            + "La ventaja de la expansión inversa no viene del algoritmo en abstracto: viene de la "
            + "PROPORCIÓN entre el tamaño del catálogo y el del conjunto accesible. Compara el "
            + "«confirmationChecks» de esta evidencia con el de la pregunta anterior — ahí está la "
            + "diferencia.\n\n"
            + "La (c) es falsa: funciona y da el resultado correcto. Solo que le cuesta más.",
            [
                new(TourProbeKind.ListObjectsComparison, "can_view sobre resource: la relación CON exclusión",
                    "user:maria", "can_view", "resource"),
                new(TourProbeKind.ListObjectsComparison, "can_edit sobre resource, para comparar: SIN exclusión",
                    "user:maria", "can_edit", "resource", ExpectedReverseReadsFewerTuples: true),
            ],
            "Fíjate en el campo «confirmationChecks» de las métricas de cada una."),

        // ── Lección 10 ──────────────────────────────────────────────────────
        new("10.1", "10", "prediction",
            "Escribes dos tuplas que hacen que el grupo A sea miembro del B y el B del A, y "
            + "preguntas por la pertenencia. ¿Qué pasa?",
            [
                new("a", "Se cuelga o desborda la pila"),
                new("b", "Responde, y cuenta el ciclo en las métricas"),
                new("c", "El sistema rechaza la segunda tupla al detectar que crearía un ciclo"),
            ],
            "b",
            "Responde. El motor lleva la pila de nodos en curso; al reencontrar el mismo par (relación, "
            + "objeto) marca el nodo como ciclo, devuelve «false» PARA ESA RAMA y sigue. Lo verás en "
            + "«metrics.cyclesDetected».\n\n"
            + "Lo que hace un sistema que no lo contempla es desbordar la pila con datos que un usuario "
            + "puede escribir — es decir, una denegación de servicio escribiendo dos tuplas.\n\n"
            + "La (c) suena razonable y sería una mala idea: detectar ciclos EN LA ESCRITURA obligaría a "
            + "recorrer el grafo entero en cada alta de tupla. Es mucho más barato tolerarlos al leer.\n\n"
            + "Puedes provocarlo tú desde la pantalla de Relaciones creando las dos tuplas cruzadas.",
            [
                new(TourProbeKind.Check, "Pertenencia legítima a un grupo anidado (sin ciclo)",
                    "user:pedro", "member", "group:auditoria", ExpectedAllowed: true),
            ]),

        new("10.2", "10", "concept",
            "LA SUTIL. El motor memoiza resultados dentro de una misma petición, pero hay dos "
            + "«false» que tiene PROHIBIDO memoizar. ¿Cuáles?",
            [
                new("a", "El de una relación inexistente y el de un tipo inexistente"),
                new("b", "El de un ciclo y el del límite de profundidad"),
                new("c", "El de una exclusión y el de una intersección fallida"),
            ],
            "b",
            "El «false» de un ciclo y el «false» del límite de profundidad. Ninguno de los dos es una "
            + "conclusión sobre el problema: son «no lo sé por aquí», no «no».\n\n"
            + "Si los memoizaras, una rama que se cortó por ciclo ENVENENARÍA otra rama distinta que sí "
            + "habría llegado a la respuesta, y el resultado dependería del orden de evaluación. Está "
            + "escrito tal cual en el código: «No se memoiza: este false no es una conclusión sobre el "
            + "problema». Es la regla que CLAUDE.md marca como no negociable.\n\n"
            + "La (a) y la (c) sí son conclusiones legítimas y se memoizan sin problema.",
            []),

        new("10.3", "10", "concept",
            "¿Por qué la memoización es por petición y no una caché entre peticiones? ¿Qué se "
            + "rompería?",
            [
                new("a", "Nada: sería seguro, simplemente no se ha implementado"),
                new("b", "Que entre peticiones el almacén puede cambiar, y un permiso cacheado es un permiso revocado que sigue funcionando"),
                new("c", "Que la traza dejaría de poder mostrarse"),
            ],
            "b",
            "Entre peticiones el almacén puede haber cambiado, y un permiso cacheado es un permiso "
            + "revocado que sigue funcionando. Dentro de una misma petición el conjunto de tuplas es "
            + "estable, así que memoizar es seguro y evita reevaluar el mismo subárbol en un grafo con "
            + "rombos (varias vías que confluyen en el mismo nodo).\n\n"
            + "El caching ENTRE peticiones existe en Zanzibar de verdad, pero va acompañado de zookies y "
            + "de un modelo de consistencia explícito. Eso es docs/06.\n\n"
            + "Fíjate en la relación con la pregunta 8.2: es la misma propiedad vista desde el otro lado.",
            []),

        // ── Lección 11 ──────────────────────────────────────────────────────
        new("11.1", "11", "concept",
            "Si RBAC se puede expresar en ReBAC, ¿qué es exactamente lo que NO se puede hacer al "
            + "revés?",
            [
                new("a", "Nada: son equivalentes, solo cambia la sintaxis"),
                new("b", "Cualquier permiso que dependa del CONTEXTO del objeto, como «el equipo que es editor del proyecto padre de esta carpeta»"),
                new("c", "Los comodines: RBAC no puede conceder a todo el mundo"),
            ],
            "b",
            "Cualquier frase donde el permiso dependa del CONTEXTO DEL OBJETO. El ejemplo canónico: «los "
            + "miembros del equipo que es editor del proyecto padre de esta carpeta».\n\n"
            + "En RBAC el permiso no tiene sujeto ni contexto — es un par (rol, permiso) sin ningún lugar "
            + "donde colgar «de esta carpeta». La única salida es multiplicar roles "
            + "(editor-proyecto-alpha, editor-proyecto-beta…) hasta que el catálogo es inmanejable. Eso es "
            + "la role explosion.\n\n"
            + "La (c) es falsa: RBAC puede tener un rol «todos» y asignárselo a todo el mundo. Es caro de "
            + "mantener, pero expresable.",
            [
                new(TourProbeKind.Check, "El ejemplo canónico, resuelto en un solo Check",
                    "user:juan", "can_edit", "resource:b", ExpectedAllowed: true),
            ]),

        new("11.2", "11", "prediction",
            "Ejecutas la comparación RBAC/ReBAC para Pedro sobre resource:a. ¿Coinciden los dos "
            + "motores?",
            [
                new("a", "Sí: los dos conceden"),
                new("b", "No: ReBAC concede y RBAC no"),
                new("c", "No: RBAC concede y ReBAC no"),
            ],
            "b",
            "ReBAC concede y RBAC no. Pedro llega por «group:seguridad#member», y en RBAC ese acceso solo "
            + "existiría si alguien lo hubiera materializado como fila — y nadie lo hizo.\n\n"
            + "En un sistema RBAC real esto se manifiesta como «a este usuario se le olvidó darle "
            + "permiso». Los dos motores discrepan justo donde hay herencia y donde hay grupos: RBAC solo "
            + "sabe lo que alguien enumeró, ReBAC lo deriva al preguntar.\n\n"
            + "Existe también el caso contrario (RBAC concede y ReBAC no): es un permiso materializado que "
            + "se quedó obsoleto, el usuario salió del equipo hace dos años y la fila sigue ahí.",
            [
                new(TourProbeKind.Check, "ReBAC: ¿Pedro puede ver Resource A?",
                    "user:pedro", "can_view", "resource:a", ExpectedAllowed: true),
                new(TourProbeKind.Check, "Y llega por el grupo, no por una tupla suya",
                    "user:pedro", "member", "group:seguridad", ExpectedAllowed: true),
            ],
            "Puedes verlo completo en la pantalla «RBAC vs ReBAC», con el razonamiento de cada motor."),

        new("11.3", "11", "concept",
            "Añades el proyecto número 501 a Acme. ¿Cuántas filas hay que escribir para que "
            + "María (admin de Acme) pueda editarlo?",
            [
                new("a", "Una en ReBAC (la de parent) y cero adicionales para María; en RBAC hay que crear y asignar roles"),
                new("b", "Una en cada modelo: ambos escalan igual"),
                new("c", "Ninguna en ReBAC: los permisos de admin son automáticos"),
            ],
            "a",
            "Una en ReBAC — «project:nuevo#parent@organization:acme», que además tendrías que escribir de "
            + "todas formas — y CERO adicionales para María: «admin from parent» la alcanza sola.\n\n"
            + "En RBAC hay que crear los roles del proyecto nuevo y asignarlos a todo el que deba "
            + "tenerlos, y no olvidarte de nadie. Ese «y no olvidarte de nadie» repetido 500 veces es el "
            + "verdadero coste de RBAC: no está en el motor, está en mantener las filas sincronizadas con "
            + "la realidad para siempre.\n\n"
            + "La (c) es un matiz importante: no son automáticos, se DEDUCEN. La diferencia es que un "
            + "permiso automático habría que materializarlo en algún momento; uno deducido no existe hasta "
            + "que alguien pregunta.",
            []),
    ];

    /// <summary>Las preguntas de una lección, en orden.</summary>
    public static IReadOnlyList<TourQuestion> ForLesson(string lessonCode) =>
        Questions.Where(question => question.LessonCode == lessonCode).ToList();
}
