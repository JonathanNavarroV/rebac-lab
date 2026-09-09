using MediatR;
using Playground.Business.Domain.Authorization;
using Playground.Business.Domain.Rbac;

namespace Playground.Business.Application.Features.Comparison;

public sealed record ModelAnswerDto(
    string Model,
    bool Allowed,
    string Reason,
    IReadOnlyList<string> Reasoning);

/// <param name="Agree">
/// <c>true</c> si los dos modelos responden lo mismo. Cuando difieren es cuando se aprende:
/// significa que RBAC no pudo expresar la política, no que uno de los dos esté mal.
/// </param>
public sealed record ComparisonDto(
    string User,
    string Action,
    string Object,
    ModelAnswerDto Rebac,
    ModelAnswerDto Rbac,
    bool Agree,
    string Analysis,
    RbacStats RbacStats,
    int RebacTupleCount);

/// <summary>
/// Responde la misma pregunta con los dos modelos y explica cómo llegó cada uno.
/// </summary>
/// <param name="Action">Acción en vocabulario RBAC: <c>view</c>, <c>edit</c> o <c>delete</c>.</param>
public sealed record CompareModelsQuery(string UserId, string Action, string ObjectRef)
    : IRequest<ComparisonDto>;

/// <summary>
/// El handler de la pantalla comparativa.
/// </summary>
/// <remarks>
/// <para>
/// Traduce la acción de RBAC (<c>edit</c>) al permiso derivado de ReBAC (<c>can_edit</c>) y
/// lanza las dos consultas. El resultado más frecuente es que coincidan — RBAC no es un modelo
/// roto, resuelve bien la mayoría de casos — y por eso los casos en los que <b>no</b> coinciden
/// son los que merecen atención.
/// </para>
/// </remarks>
public sealed class CompareModelsQueryHandler(
    IAccessControlService accessControl,
    IRbacEngine rbac) : IRequestHandler<CompareModelsQuery, ComparisonDto>
{
    private static readonly Dictionary<string, string> ActionToRelation = new(StringComparer.OrdinalIgnoreCase)
    {
        ["view"] = "can_view",
        ["edit"] = "can_edit",
        ["delete"] = "can_delete",
        ["publish"] = "can_publish",
    };

    public async Task<ComparisonDto> Handle(CompareModelsQuery request, CancellationToken cancellationToken)
    {
        var relation = ActionToRelation.GetValueOrDefault(request.Action, $"can_{request.Action}");
        var subject = $"user:{request.UserId}";

        var rebacDecision = await accessControl.CanAsync(subject, relation, request.ObjectRef, cancellationToken);
        var rbacDecision = await rbac.CheckAsync(request.UserId, request.Action, request.ObjectRef, cancellationToken);
        var stats = await rbac.GetStatsAsync(cancellationToken);

        var rebacAnswer = new ModelAnswerDto(
            "ReBAC",
            rebacDecision.Allowed,
            rebacDecision.Reason,
            BuildRebacReasoning(rebacDecision));

        var rbacAnswer = new ModelAnswerDto(
            "RBAC",
            rbacDecision.Allowed,
            rbacDecision.Reason,
            rbacDecision.Trace.Select(step => $"{(step.Matched ? "✓" : "·")} {step.Description}").ToList());

        var agree = rebacDecision.Allowed == rbacDecision.Allowed;

        return new ComparisonDto(
            subject,
            request.Action,
            request.ObjectRef,
            rebacAnswer,
            rbacAnswer,
            agree,
            BuildAnalysis(agree, rebacDecision.Allowed, rbacDecision, stats),
            stats,
            RebacTupleCount: 0);
    }

    private static IReadOnlyList<string> BuildRebacReasoning(AccessDecision decision) =>
    [
        "Se pregunta al módulo de control de acceso, que no conoce roles ni permisos.",
        "El motor recorre el modelo de relaciones desde el objeto hacia el sujeto.",
        decision.Reason,
    ];

    private static string BuildAnalysis(bool agree, bool rebacAllowed, RbacDecision rbac, RbacStats stats)
    {
        if (!agree)
        {
            return rebacAllowed
                ? "Los modelos DIFIEREN: ReBAC concede y RBAC no. Casi siempre significa que el acceso "
                  + "llega por una relación que RBAC no puede expresar (pertenecer a un equipo que tiene "
                  + "acceso, o heredar de una carpeta padre) y que nadie ha materializado todavía como "
                  + "fila de permiso. En un sistema RBAC real, esto se manifiesta como \"a este usuario "
                  + "se le olvidó darle permiso\"."
                : "Los modelos DIFIEREN: RBAC concede y ReBAC no. Suele ser un permiso materializado que "
                  + "se quedó obsoleto — el usuario salió del equipo, o el recurso se movió de carpeta, "
                  + "y la fila de permiso sigue ahí. Es el fallo de seguridad típico de RBAC: nadie "
                  + "recuerda limpiar lo que se concedió hace dos años.";
        }

        var shared = $"Los dos modelos coinciden ({(rebacAllowed ? "ALLOW" : "DENY")}), que es lo normal: "
                     + "RBAC resuelve bien la mayoría de los casos y no es un modelo roto. ";

        return shared
               + $"La diferencia está en el coste de mantenerlo: RBAC necesita {stats.TotalRows} filas "
               + $"({stats.Roles} roles, {stats.Permissions} permisos, {stats.UserRoleAssignments} asignaciones) "
               + "para expresar este mismo escenario, porque tiene que enumerar cada objeto concreto. "
               + $"Y añadir UN solo recurso nuevo a Project Alpha exigiría insertar {stats.RowsToAddOneResource} "
               + "filas más, una por cada rol que debería alcanzarlo. En ReBAC ese recurso nuevo necesita "
               + "exactamente UNA tupla: la que dice de qué cuelga. Todo lo demás se deduce.";
    }
}
