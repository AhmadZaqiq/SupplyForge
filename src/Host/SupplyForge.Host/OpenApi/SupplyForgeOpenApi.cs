using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace SupplyForge.Host.OpenApi;

/// <summary>
/// INT-052 / ADR-006 §8–9: the generated OpenAPI document describes both
/// authenticators of the single pipeline — Bearer JWT for interactive users
/// and the frozen <c>X-Api-Key</c> header (ADR-009) for Integration Clients.
/// Security requirements are applied per operation from the endpoint's own
/// authorization metadata (PLT-022): only endpoints that declare
/// <see cref="IAuthorizeData"/> get a requirement, so liveness, login, and
/// refresh stay honestly unsecured in the contract.
/// </summary>
public static class SupplyForgeOpenApi
{
    public const string BearerSchemeId = "Bearer";
    public const string ApiKeySchemeId = "X-Api-Key";

    /// <summary>Wires the SupplyForge document/operation transformers onto <c>AddOpenApi</c>.</summary>
    public static OpenApiOptions UseSupplyForgeConventions(this OpenApiOptions options)
    {
        options.AddDocumentTransformer(AddSecuritySchemes);
        options.AddOperationTransformer(ApplyEndpointSecurity);
        return options;
    }

    /// <summary>Registers both auth schemes in <c>components.securitySchemes</c>.</summary>
    private static Task AddSecuritySchemes(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var components = document.Components ?? new OpenApiComponents();
        components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description =
                "Interactive user access token (ADR-004/006): Authorization: Bearer <access-token>.",
        };
        components.SecuritySchemes[ApiKeySchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.ApiKey,
            Name = ApiKeySchemeId,
            In = ParameterLocation.Header,
            Description =
                "Integration Client API key (ADR-009, frozen header): X-Api-Key: <secret>.",
        };
        document.Components = components;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Applies a Bearer requirement only where the endpoint declares
    /// authorization metadata; AllowAnonymous and metadata-free endpoints get
    /// nothing. Endpoint-site transformers (e.g. the X-Api-Key self-check
    /// route) can add their own scheme on top.
    /// </summary>
    private static Task ApplyEndpointSecurity(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        if (metadata.OfType<IAllowAnonymous>().Any())
        {
            return Task.CompletedTask;
        }

        if (metadata.OfType<IAuthorizeData>().Any())
        {
            operation.Security ??= [];
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = [],
            });
        }

        return Task.CompletedTask;
    }
}
