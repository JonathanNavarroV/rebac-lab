namespace Playground.AccessControl.Domain.Model;

/// <summary>
/// Regla de reescritura de usersets: cómo se calcula quién tiene una relación.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aquí está todo el poder expresivo de Zanzibar.</b> Solo hay cinco variantes, y con
/// ellas se construyen jerarquías, herencia, grupos anidados, compartición y roles. No hay
/// más. Si algo no se puede expresar combinando estas cinco, no se puede expresar en ReBAC
/// (y probablemente necesites ABAC — ver <c>docs/05</c>).
/// </para>
/// <list type="table">
///   <listheader><term>Variante</term><description>DSL y qué resuelve</description></listheader>
///   <item><term><see cref="This"/></term><description>
///     <c>[user, team#member]</c> — lee las tuplas escritas directamente. Es el caso base:
///     el único que consulta la base de datos. Todas las demás variantes acaban delegando aquí.
///   </description></item>
///   <item><term><see cref="ComputedUserset"/></term><description>
///     <c>define can_edit: editor</c> — "quien tenga <i>esta otra</i> relación sobre
///     <i>el mismo objeto</i>". Permite separar la relación que se escribe (<c>editor</c>)
///     del permiso que se comprueba (<c>can_edit</c>), que es una práctica muy recomendable:
///     el negocio pregunta por <c>can_edit</c> y tú puedes cambiar cómo se concede sin
///     tocar el negocio.
///   </description></item>
///   <item><term><see cref="TupleToUserset"/></term><description>
///     <c>define can_view: can_view from parent</c> — <b>la variante estrella</b>. "Sigue la
///     relación <c>parent</c> de este objeto y pregunta <c>can_view</c> allí". Esto es la
///     herencia y las jerarquías. Si <c>parent</c> puede apuntar al propio tipo
///     (<c>folder</c> con <c>parent: [folder]</c>), la regla es recursiva y cubre árboles de
///     profundidad arbitraria con una sola línea de modelo.
///   </description></item>
///   <item><term><see cref="Union"/></term><description>
///     <c>define can_edit: editor or owner</c> — cualquiera basta. Es lo que produce los
///     <b>múltiples caminos de autorización</b>: si Juan es a la vez owner y miembro de un
///     equipo editor, hay dos ramas que dicen ALLOW.
///   </description></item>
///   <item><term><see cref="Intersection"/> y <see cref="Exclusion"/></term><description>
///     <c>a and b</c> / <c>a but not b</c> — exigir dos condiciones, o revocar. La exclusión
///     es la que rompe la expansión inversa ingenua de ListObjects; ver <c>docs/04</c>.
///   </description></item>
/// </list>
/// <para>
/// Correspondencia con el paper (sección 2.3, <i>userset rewrite rules</i>): <c>_this</c>,
/// <c>computed_userset</c>, <c>tuple_to_userset</c>, <c>union</c>, <c>intersection</c> y
/// <c>exclusion</c>. Mantenemos los nombres del paper a propósito para que puedas leerlo
/// con este código al lado.
/// </para>
/// </remarks>
public abstract record UsersetRewrite
{
    /// <summary>Nombre de la regla en la terminología del paper. Se usa en la traza y en la UI.</summary>
    public abstract string Kind { get; }

    /// <summary>Cómo se escribiría esta regla en el DSL. Alimenta la pantalla del modelo.</summary>
    public abstract string ToDsl();

    /// <summary>
    /// Asignación directa admitida por una regla <see cref="This"/>: qué clase de sujetos
    /// pueden escribirse en una tupla de esta relación.
    /// </summary>
    /// <remarks>
    /// Esto es el "tipado" del modelo, y sirve para dos cosas: rechazar tuplas que no tienen
    /// sentido (<c>project:alpha#editor@project:beta</c>) y, sobre todo, para que la
    /// expansión inversa sepa qué buscar sin adivinar.
    /// </remarks>
    public sealed record DirectAssignment(string SubjectType, string? SubjectRelation, bool Wildcard)
    {
        /// <summary>Un individuo de ese tipo: <c>user</c>.</summary>
        public static DirectAssignment OfType(string type) => new(type, null, false);

        /// <summary>Un userset: <c>team#member</c>.</summary>
        public static DirectAssignment OfUserset(string type, string relation) => new(type, relation, false);

        /// <summary>Cualquiera de ese tipo: <c>user:*</c>.</summary>
        public static DirectAssignment OfWildcard(string type) => new(type, null, true);

        public bool Accepts(SubjectRef subject)
        {
            if (subject.Type != SubjectType)
                return false;

            if (Wildcard)
                return true;

            return subject.Relation == SubjectRelation;
        }

        public string ToDsl() => (Wildcard, SubjectRelation) switch
        {
            (true, _) => $"{SubjectType}:*",
            (_, not null) => $"{SubjectType}#{SubjectRelation}",
            _ => SubjectType,
        };

        public override string ToString() => ToDsl();
    }

    /// <summary>
    /// Caso base: las tuplas escritas explícitamente para esta relación sobre este objeto.
    /// En el DSL es la lista entre corchetes.
    /// </summary>
    public sealed record This(IReadOnlyList<DirectAssignment> Allowed) : UsersetRewrite
    {
        public override string Kind => "_this";

        public override string ToDsl() =>
            Allowed.Count == 0
                ? "[]"
                : $"[{string.Join(", ", Allowed.Select(a => a.ToDsl()))}]";
    }

    /// <summary>
    /// Otra relación sobre <b>el mismo objeto</b>. <c>define can_delete: owner</c>.
    /// </summary>
    public sealed record ComputedUserset(string Relation) : UsersetRewrite
    {
        public override string Kind => "computed_userset";

        public override string ToDsl() => Relation;
    }

    /// <summary>
    /// Sigue una relación de este objeto (el <i>tupleset</i>) y evalúa otra relación en los
    /// objetos encontrados. <c>define can_edit: can_edit from parent</c>.
    /// </summary>
    /// <param name="Tupleset">
    /// La relación que se recorre para encontrar los objetos "de arriba". Casi siempre
    /// <c>parent</c>, pero podría ser cualquiera (<c>organization</c>, <c>owner_team</c>...).
    /// </param>
    /// <param name="ComputedRelation">
    /// La relación que se pregunta en cada objeto encontrado. Si coincide con la relación
    /// que se está definiendo, la regla es recursiva y recorre la jerarquía completa.
    /// </param>
    public sealed record TupleToUserset(string Tupleset, string ComputedRelation) : UsersetRewrite
    {
        public override string Kind => "tuple_to_userset";

        public override string ToDsl() => $"{ComputedRelation} from {Tupleset}";
    }

    /// <summary>Basta con que una rama conceda acceso. <c>a or b or c</c>.</summary>
    public sealed record Union(IReadOnlyList<UsersetRewrite> Children) : UsersetRewrite
    {
        public override string Kind => "union";

        public override string ToDsl() => string.Join(" or ", Children.Select(c => c.ToDsl()));
    }

    /// <summary>Todas las ramas deben conceder acceso. <c>a and b</c>.</summary>
    public sealed record Intersection(IReadOnlyList<UsersetRewrite> Children) : UsersetRewrite
    {
        public override string Kind => "intersection";

        public override string ToDsl() => string.Join(" and ", Children.Select(c => c.ToDsl()));
    }

    /// <summary>
    /// Concede si <see cref="Base"/> concede y <see cref="Subtract"/> no.
    /// <c>viewer but not banned</c>.
    /// </summary>
    public sealed record Exclusion(UsersetRewrite Base, UsersetRewrite Subtract) : UsersetRewrite
    {
        public override string Kind => "exclusion";

        public override string ToDsl() => $"{Base.ToDsl()} but not {Subtract.ToDsl()}";
    }
}
