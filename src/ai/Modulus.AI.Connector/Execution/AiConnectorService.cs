namespace Modulus.AI.Connector.Execution;

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Modulus.AI.Connector.Capabilities;
using Modulus.AI.Connector.Contract;
using Modulus.AI.Connector.Data;
using Modulus.Core.Abstractions;
using Modulus.Core.Abstractions.Exceptions;
using Modulus.Mediator.Abstractions;

/// <summary>
/// Runs the connector calls. Every call enters the instance's company (never the host context), runs under
/// <see cref="ModulusAiConnectorOptions.CallTimeout"/>, maps exceptions to typed errors and is recorded in the security
/// audit trail (who, which instance, what, the outcome; never data). Queries go through <see cref="IMediator"/> as the
/// asserted user, so permissions, features, validation and the tenant filter all apply. Nothing is cached (AD-13).
/// </summary>
internal sealed partial class AiConnectorService(
    IServiceProvider services,
    IMediator mediator,
    ICurrentUser currentUser,
    AiCapabilityRegistry registry,
    AiResultProjector projector,
    IOptions<ModulusAiConnectorOptions> options,
    ISecurityAuditLog audit,
    ILogger<AiConnectorService> logger)
{
    private static readonly ConcurrentDictionary<Type, Func<IMediator, object, CancellationToken, Task<object?>>> Invokers = new();

    public Task<IResult> ExecuteAsync(HttpContext http, string name, CapabilityExecuteRequest? body)
        => RunAsync(http, "connector.capability", name, async ct =>
        {
            if (!registry.TryGetCapability(name, out var capability))
                return Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, $"No capability '{name}'.");
            if (capability.Generated != AiGeneratedCapability.None)
                return await ExecuteGeneratedAsync(capability, body?.Args, ct);

            var query = body?.Args switch
            {
                { ValueKind: JsonValueKind.Object } args => args.Deserialize(capability.QueryType, ConnectorJson.Arguments),
                null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined }
                    => JsonSerializer.Deserialize("{}", capability.QueryType, ConnectorJson.Arguments),
                _ => throw new JsonException("The arguments must be an object."),
            } ?? throw new JsonException("The arguments are null.");

            var response = await QueryAsync(query, capability.ResponseType, ct);
            var resource = capability.ResourceType is null ? null
                : registry.TryGetResource(capability.ResourceType, out var r) ? r : null;
            return Json(projector.Project(response, capability.ItemType, resource, options.Value.MaxResults));
        });

    public Task<IResult> GetResourceAsync(HttpContext http, ResourceGetRequest request)
        => RunAsync(http, "connector.resource", $"{request.ResourceType}:{request.ResourceId}", async ct =>
        {
            if (!registry.TryGetResource(request.ResourceType, out var resource))
                return Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, $"No resource type '{request.ResourceType}'.");

            var item = await LookupAsync(resource, request.ResourceId, ct);
            return item is null
                ? Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "No such record.")
                : Json(projector.ProjectOne(item, resource));
        });

    public Task<IResult> ScopeAsync(HttpContext http)
        => RunAsync(http, "connector.authz.scope", null, ct =>
        {
            var call = AiConnectorCall.Of(http)!;
            var dataScopes = new Dictionary<string, string[]>(StringComparer.Ordinal);
            if (call.Instance.TenantId is { } tenantId)
                dataScopes["company"] = [tenantId.ToString("D")];
            if (services.GetService<ICurrentDataScope>() is { IsUnrestricted: false } dataScope)
                dataScopes["orgUnits"] = [.. dataScope.OrgUnitIds.Select(id => id.ToString("D"))];

            var fieldPolicies = new SortedDictionary<string, FieldAccessPolicy>(StringComparer.Ordinal);
            foreach (var resource in registry.Resources)
                AddPolicies(fieldPolicies, resource.ResourceType, resource.ItemType);
            foreach (var capability in registry.Capabilities.Where(c => c.ResourceType is null))
                AddPolicies(fieldPolicies, capability.Name, capability.Fields);

            var scope = new AccessScope(
                call.Instance.AppInstanceId,
                call.User.Roles,
                [.. currentUser.Permissions.Order(StringComparer.Ordinal)],
                dataScopes,
                fieldPolicies,
                (int)options.Value.ScopeTtl.TotalSeconds,
                RevocationKeys.For(call.Instance.AppInstanceId));
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Json(scope));
        });

    public Task<IResult> CheckResourcesAsync(HttpContext http, ResourcesCheckRequest request)
        => RunAsync(http, "connector.authz.resources", $"{request.Resources?.Count ?? 0} records", async ct =>
        {
            if (request.Resources is not { Count: > 0 } references)
                return Json(new ResourcesCheckResult([]));
            if (references.Count > options.Value.MaxBatchSize)
                return Error(StatusCodes.Status400BadRequest, ConnectorErrorCodes.InvalidRequest,
                    $"At most {options.Value.MaxBatchSize} records per call.");

            var decisions = new List<ResourceDecision>(references.Count);
            foreach (var reference in references)
            {
                var allowed = false;
                if (registry.TryGetResource(reference.ResourceType, out var resource))
                {
                    try
                    {
                        allowed = await LookupAsync(resource, reference.ResourceId, ct) is not null;
                    }
                    catch (Exception ex) when (IsDenial(ex) || ex is NotFoundException)
                    {
                        allowed = false;
                    }
                }

                decisions.Add(new ResourceDecision(reference, allowed));
            }

            return Json(new ResourcesCheckResult(decisions));
        });

    public Task<IResult> CheckFieldsAsync(HttpContext http, FieldsCheckRequest request)
        => RunAsync(http, "connector.authz.fields", request.Resource?.ResourceType, ct =>
        {
            if (request.Resource is null || !registry.TryGetResource(request.Resource.ResourceType, out var resource))
                return Task.FromResult(Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "No such resource type."));

            var readable = AiFieldCatalog.For(resource.ItemType)
                .Where(projector.CanRead)
                .Select(f => f.Name)
                .ToHashSet(StringComparer.Ordinal);
            var allowed = (request.Fields ?? []).Where(readable.Contains).Distinct(StringComparer.Ordinal).ToList();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Json(new FieldsCheckResult(allowed)));
        });

    /// <summary>
    /// <c>GET /extract</c>: the indexed records of <paramref name="instance"/>'s company, in resource-type then key order,
    /// read as the indexing identity (which the caller has already made the request's user). A record the identity may
    /// not see is skipped; a resource type it may not read at all fails the call (<c>DENIED</c>), so a missing grant is
    /// noticed instead of producing an empty index.
    /// </summary>
    public Task<IResult> ExtractAsync(HttpContext http, AiAppInstanceOptions instance, string? resourceType, string? cursor, int? limit)
        => RunAsync(http, IndexerCaller(instance), "connector.extract", resourceType, async ct =>
        {
            if (services.GetService<IAiEntitySource>() is not { } source)
                return Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "This app does not offer extraction.");

            var types = registry.Indexed.ToList();
            if (resourceType is not null)
                types = [.. types.Where(t => t.ResourceType == resourceType)];
            if (types.Count == 0)
                return Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "No indexed resource type.");

            var (startType, after) = ExtractCursor.Decode(cursor);
            var index = startType is null ? 0 : types.FindIndex(t => t.ResourceType == startType);
            if (index < 0)
                throw new FormatException("The cursor names another resource type.");

            var budget = PageSize(limit);
            var records = new List<IndexedResource>();
            string? next = null;
            while (index < types.Count)
            {
                var type = types[index];
                var keys = await source.ListKeysAsync(type.EntityType, after, budget, ct);
                budget -= keys.Count;
                var found = await LookupManyAsync(type.Resource, keys, ct);
                foreach (var key in keys)
                {
                    if (found.TryGetValue(key, out var item) && item is not null)
                        records.Add(projector.Index(item, key, type.Resource, Access(instance, type.Resource)));
                }

                if (budget > 0 || keys.Count == 0)
                {
                    // The type is exhausted: the next page starts at the next type.
                    (index, after) = (index + 1, null);
                    next = index < types.Count ? ExtractCursor.Encode(types[index].ResourceType, null) : null;
                    if (budget > 0)
                        continue;
                }
                else
                {
                    next = ExtractCursor.Encode(type.ResourceType, keys[^1]);
                }

                break;
            }

            return Json(new ExtractPage(records, next));
        });

    /// <summary>
    /// <c>GET /changes</c>: the journaled changes of <paramref name="instance"/>'s company after <paramref name="since"/>,
    /// the last change of each record only. An upsert is re-read through the lookup as the indexing identity; a record
    /// it can no longer see (deleted, soft-deleted, moved out of its scope) becomes a tombstone.
    /// </summary>
    public Task<IResult> ChangesAsync(HttpContext http, AiAppInstanceOptions instance, string? since, int? limit)
        => RunAsync(http, IndexerCaller(instance), "connector.changes", null, async ct =>
        {
            if (services.GetService<IAiChangeFeed>() is not { } feed)
                return Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "This app does not offer a change feed.");

            var settled = services.GetRequiredService<TimeProvider>().GetUtcNow() - options.Value.Indexing.ChangesSettleDelay;
            var journal = await feed.ReadAsync(instance.TenantId ?? Guid.Empty, since, PageSize(limit), settled, ct);

            // The last change of each record wins; the record is read once.
            var latest = new Dictionary<(string Type, string Id), AiJournalChange>();
            foreach (var change in journal.Changes)
            {
                latest.Remove((change.ResourceType, change.ResourceId));
                latest[(change.ResourceType, change.ResourceId)] = change;
            }

            // One read per resource type for all of its upserts (a batch lookup when the type has one).
            var reads = new Dictionary<(string Type, string Id), object?>();
            foreach (var group in latest.Values.Where(c => c.Kind != AiChangeKind.Delete).GroupBy(c => c.ResourceType))
            {
                if (!registry.TryGetIndexed(group.Key, out var groupType))
                    continue;
                foreach (var (id, item) in await LookupManyAsync(groupType.Resource, [.. group.Select(c => c.ResourceId)], ct))
                    reads[(group.Key, id)] = item;
            }

            var changes = new List<ResourceChange>(latest.Count);
            foreach (var change in latest.Values.OrderBy(c => c.OccurredAt))
            {
                if (!registry.TryGetIndexed(change.ResourceType, out var type))
                    continue;

                var reference = new ResourceReference(change.ResourceType, change.ResourceId);
                var item = change.Kind == AiChangeKind.Delete ? null : reads.GetValueOrDefault((change.ResourceType, change.ResourceId));
                changes.Add(item is null
                    ? new ResourceChange(ResourceChangeKind.Tombstone, reference, null, change.OccurredAt)
                    : new ResourceChange(
                        ResourceChangeKind.Upsert,
                        reference,
                        projector.Index(item, change.ResourceId, type.Resource, Access(instance, type.Resource)),
                        change.OccurredAt));
            }

            return Json(new ChangesPage(changes, journal.Cursor, journal.HasMore));
        });

    // The generated Search and Calculate of an [AiQueryable] entity: the permission, then only fields the user may read
    // (filtering or grouping on a masked field would reveal it), then the query through the entity source.
    private async Task<IResult> ExecuteGeneratedAsync(AiCapabilityDescriptor capability, JsonElement? args, CancellationToken ct)
    {
        var queryable = capability.Queryable!;
        if (!currentUser.IsAuthenticated || !currentUser.HasPermission(queryable.Permission))
            throw new ForbiddenException(queryable.Permission);
        var source = services.GetService<IAiEntitySource>()
            ?? throw new InvalidOperationException("No AI entity source is registered.");
        var max = options.Value;

        if (capability.Generated == AiGeneratedCapability.Search)
        {
            var search = Bind<AiSearchArguments>(args);
            RequireReadable(queryable, (search.Filters ?? []).Select(f => f.Field).Concat((search.Sort ?? []).Select(s => s.Field)));
            var take = search.Take ?? max.MaxResults;
            if (take < 1 || take > max.MaxResults)
                throw new ArgumentException($"take must be between 1 and {max.MaxResults}.");

            var rows = await source.ListAsync(
                queryable.EntityType,
                new AiEntityQuery(AiEntityQueries.Filter(queryable, search.Filters, max.MaxFilters), AiEntityQueries.Sort(queryable, search.Sort), take + 1),
                ct);
            var resource = capability.ResourceType is not null && registry.TryGetResource(capability.ResourceType, out var r) ? r : null;
            return Json(projector.Project(rows, capability.Fields, resource, take));
        }

        var calculate = Bind<AiCalculateArguments>(args);
        RequireReadable(queryable, (calculate.Filters ?? []).Select(f => f.Field).Append(calculate.Field).Append(calculate.GroupBy));
        var aggregates = await source.AggregateAsync(
            queryable.EntityType,
            new AiAggregateQuery(
                AiEntityQueries.Filter(queryable, calculate.Filters, max.MaxFilters),
                calculate.GroupBy is null ? null : AiEntityQueries.Selector(queryable, calculate.GroupBy),
                calculate.Aggregate,
                AiEntityQueries.AggregateField(queryable, calculate),
                max.MaxGroups + 1),
            ct);

        var records = aggregates.Take(max.MaxGroups).Select(row =>
        {
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                [AiFieldCatalog.ValueField] = JsonSerializer.SerializeToElement(row.Value, ConnectorJson.Options),
            };
            if (calculate.GroupBy is not null)
                fields[AiQuerySchemas.GroupField] = JsonSerializer.SerializeToElement(row.Group, ConnectorJson.Options);
            return new AppResource(null, fields, null);
        }).ToList();
        return Json(new CapabilityResult(records, aggregates.Count > max.MaxGroups));
    }

    private void RequireReadable(AiQueryableDescriptor queryable, IEnumerable<string?> fields)
    {
        foreach (var field in AiEntityQueries.Referenced(queryable, fields))
        {
            if (!projector.CanRead(field))
                throw new ForbiddenException($"{queryable.ResourceType}.{field.Name}");
        }
    }

    private static T Bind<T>(JsonElement? args)
        => (args switch
        {
            { ValueKind: JsonValueKind.Object } value => value.Deserialize<T>(ConnectorJson.Arguments),
            null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => JsonSerializer.Deserialize<T>("{}", ConnectorJson.Arguments),
            _ => throw new JsonException("The arguments must be an object."),
        }) ?? throw new JsonException("The arguments are null.");

    private int PageSize(int? limit)
    {
        var max = options.Value.Indexing.PageSize;
        return limit is null ? max : limit is >= 1 ? Math.Min(limit.Value, max) : throw new ArgumentException("limit must be positive.");
    }

    private static ResourceAccess Access(AiAppInstanceOptions instance, AiResourceDescriptor resource)
        => new(
            resource.RequiredPermission is null ? [] : [resource.RequiredPermission],
            instance.TenantId is { } company
                ? new Dictionary<string, string[]>(StringComparer.Ordinal) { ["company"] = [company.ToString("D")] }
                : new Dictionary<string, string[]>(StringComparer.Ordinal));

    private async Task<object?> LookupOrNullAsync(AiResourceDescriptor resource, string id, CancellationToken ct)
    {
        try
        {
            return await LookupAsync(resource, id, ct);
        }
        catch (NotFoundException)
        {
            return null;
        }
    }

    private static AiCaller IndexerCaller(AiAppInstanceOptions instance)
        => new(instance, AiIndexer.Actor, null, null);

    private void AddPolicies(IDictionary<string, FieldAccessPolicy> policies, string prefix, Type itemType)
        => AddPolicies(policies, prefix, AiFieldCatalog.For(itemType));

    private void AddPolicies(IDictionary<string, FieldAccessPolicy> policies, string prefix, IReadOnlyList<AiField> fields)
    {
        foreach (var field in fields.Where(f => !f.IsSecret))
            policies[$"{prefix}.{field.Name}"] = projector.CanRead(field) ? FieldAccessPolicy.Allow : FieldAccessPolicy.Deny;
    }

    // Reads many records of one resource type: one query through the type's batch lookup when it declares one, else one lookup each.
    // The result maps each requested id (as given) to its record; an id that is not found or not visible is absent.
    private async Task<IReadOnlyDictionary<string, object?>> LookupManyAsync(
        AiResourceDescriptor resource, IReadOnlyList<string> ids, CancellationToken ct)
    {
        var found = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (ids.Count == 0)
            return found;

        if (resource.Batch is not { } batch)
        {
            foreach (var id in ids)
            {
                if (await LookupOrNullAsync(resource, id, ct) is { } item)
                    found[id] = item;
            }

            return found;
        }

        var typed = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            if (ParseKey(resource.IdType, id) is { } key)
                typed[id] = key;
        }

        if (typed.Count == 0)
            return found;

        var distinct = typed.Values.Distinct().ToList();
        var list = Array.CreateInstance(resource.IdType, distinct.Count);
        for (var i = 0; i < distinct.Count; i++)
            list.SetValue(distinct[i], i);
        var argument = batch.ListType.IsArray || batch.ListType.IsAssignableFrom(list.GetType())
            ? list
            : Activator.CreateInstance(batch.ListType, list)!;

        object? response;
        try
        {
            response = await QueryAsync(batch.Constructor.Invoke([argument]), batch.ResponseType, ct);
        }
        catch (NotFoundException)
        {
            return found;
        }

        var byKey = new Dictionary<object, object>();
        foreach (var item in AiFieldCatalog.Items(response))
        {
            if (item is not null && batch.IdProperty.GetValue(item) is { } key)
                byKey[key] = item;
        }

        foreach (var (id, key) in typed)
        {
            if (byKey.TryGetValue(key, out var item))
                found[id] = item;
        }

        return found;
    }

    private static object? ParseKey(Type idType, string id)
        => idType == typeof(string) ? id
            : idType == typeof(Guid) ? (Guid.TryParse(id, out var guid) ? guid : null)
            : idType == typeof(int) ? (int.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : null)
            : long.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null;

    private async Task<object?> LookupAsync(AiResourceDescriptor resource, string id, CancellationToken ct)
    {
        object? key = resource.IdType == typeof(string) ? id
            : resource.IdType == typeof(Guid) ? (Guid.TryParse(id, out var guid) ? guid : null)
            : resource.IdType == typeof(int) ? (int.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var i) ? i : null)
            : long.TryParse(id, System.Globalization.CultureInfo.InvariantCulture, out var l) ? l : null;
        if (key is null)
            return null; // Not an id this type can have: no such record.

        var query = resource.Constructor.Invoke([key]);
        var response = await QueryAsync(query, resource.ResponseType, ct);
        return AiFieldCatalog.Items(response).FirstOrDefault();
    }

    private Task<object?> QueryAsync(object query, Type responseType, CancellationToken ct)
        => Invokers.GetOrAdd(responseType, static type => typeof(AiConnectorService)
                .GetMethod(nameof(InvokeAsync), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type)
                .CreateDelegate<Func<IMediator, object, CancellationToken, Task<object?>>>())
            (mediator, query, ct);

    private static async Task<object?> InvokeAsync<TResponse>(IMediator mediator, object query, CancellationToken ct)
        => await mediator.QueryAsync((IQuery<TResponse>)query, ct).ConfigureAwait(false);

    private Task<IResult> RunAsync(HttpContext http, string action, string? target, Func<CancellationToken, Task<IResult>> work)
    {
        var call = AiConnectorCall.Of(http);
        return call is null
            ? Task.FromResult(Error(StatusCodes.Status401Unauthorized, ConnectorErrorCodes.Denied, "The request could not be authenticated."))
            : RunAsync(http, new AiCaller(call.Instance, call.User.UserId.ToString("D"), call.Envelope.CorrelationId, call.Envelope.Id), action, target, work);
    }

    // Enters the company, applies the timeout, maps failures to typed errors and audits the call, all in this one async
    // frame: the ambient tenant is an AsyncLocal, so it reaches the work only when set by the method that awaits it.
    private async Task<IResult> RunAsync(
        HttpContext http, AiCaller call, string action, string? target, Func<CancellationToken, Task<IResult>> work)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
        timeout.CancelAfter(options.Value.CallTimeout);

        IResult result;
        string outcome;
        string? code = null;
        try
        {
            TenantInfo? tenant = call.Instance.TenantId is { } tenantId
                ? await services.VerifyTenantAsync(tenantId, timeout.Token)
                : null;
            using var company = tenant is null ? null : services.EnterTenant(tenant);

            result = await work(timeout.Token);
            outcome = result is IStatusCodeHttpResult { StatusCode: >= 400 } ? SecurityAuditOutcomes.Denied : SecurityAuditOutcomes.Success;
        }
        catch (OperationCanceledException) when (!http.RequestAborted.IsCancellationRequested)
        {
            (result, code, outcome) = (Error(StatusCodes.Status503ServiceUnavailable, ConnectorErrorCodes.Unavailable, "The call timed out."),
                ConnectorErrorCodes.Unavailable, SecurityAuditOutcomes.Denied);
        }
        catch (Exception ex) when (IsDenial(ex))
        {
            (result, code, outcome) = (Error(StatusCodes.Status403Forbidden, ConnectorErrorCodes.Denied, "The user may not do this."),
                ConnectorErrorCodes.Denied, SecurityAuditOutcomes.Denied);
        }
        catch (NotFoundException)
        {
            (result, code, outcome) = (Error(StatusCodes.Status404NotFound, ConnectorErrorCodes.NotFound, "No such record."),
                ConnectorErrorCodes.NotFound, SecurityAuditOutcomes.Denied);
        }
        catch (Exception ex) when (ex is JsonException or ValidationException or ArgumentException or NotSupportedException or FormatException)
        {
            var message = ex switch
            {
                ValidationException validation => string.Join("; ", validation.Errors),
                ArgumentException argument => argument.Message,
                FormatException => "The cursor is not valid.",
                _ => "The arguments do not match the input schema.",
            };
            (result, code, outcome) = (Error(StatusCodes.Status400BadRequest, ConnectorErrorCodes.InvalidRequest, message),
                ConnectorErrorCodes.InvalidRequest, SecurityAuditOutcomes.Denied);
        }
        catch (Exception ex) when (!http.RequestAborted.IsCancellationRequested)
        {
            logger.LogError(ex, "AI connector call {Action} failed.", action);
            (result, code, outcome) = (Error(StatusCodes.Status503ServiceUnavailable, ConnectorErrorCodes.Unavailable, "The app could not answer."),
                ConnectorErrorCodes.Unavailable, SecurityAuditOutcomes.Denied);
        }

        audit.Record(new SecurityAuditEvent
        {
            Category = SecurityAuditCategories.Ai,
            Action = action,
            Outcome = outcome,
            TenantId = call.Instance.TenantId,
            Actor = call.Actor,
            Target = target,
            CorrelationId = call.CorrelationId,
            Details = new Dictionary<string, string?>
            {
                ["appInstanceId"] = call.Instance.AppInstanceId,
                ["envelopeId"] = call.EnvelopeId,
                ["error"] = code ?? (result is IStatusCodeHttpResult { StatusCode: >= 400 } ? "status" : null),
            },
        });

        return result;
    }

    /// <summary>Who a call runs as, for the audit trail.</summary>
    private sealed record AiCaller(AiAppInstanceOptions Instance, string Actor, string? CorrelationId, string? EnvelopeId);

    private static bool IsDenial(Exception ex)
        => ex is UnauthorizedException or ForbiddenException or FeatureDisabledException
            or TenantContextRejectedException or CrossTenantWriteException;

    private static IResult Json<T>(T value) => Results.Json(value, ConnectorJson.Options);

    private static IResult Error(int status, string code, string message)
        => Results.Json(new ConnectorError(code, message), ConnectorJson.Options, statusCode: status);
}

/// <summary>The resumable position of <c>GET /extract</c>: a resource type and the last key returned of it.</summary>
internal static class ExtractCursor
{
    public static string Encode(string resourceType, string? afterKey)
        => System.Buffers.Text.Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new Position(resourceType, afterKey)));

    /// <summary>Reads a cursor; null or empty starts at the beginning.</summary>
    /// <exception cref="FormatException">Not a cursor this connector issued.</exception>
    public static (string? ResourceType, string? AfterKey) Decode(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor))
            return (null, null);
        try
        {
            var position = JsonSerializer.Deserialize<Position>(System.Buffers.Text.Base64Url.DecodeFromChars(cursor));
            return position is { T.Length: > 0 } ? (position.T, position.K) : throw new FormatException("Invalid cursor.");
        }
        catch (Exception ex) when (ex is JsonException or FormatException)
        {
            throw new FormatException("Invalid cursor.", ex);
        }
    }

    private sealed record Position(string T, string? K);
}

/// <summary>The revocation keys the connector hands out: one per app instance.</summary>
/// <remarks>
/// Coarse on purpose: any access change in a company drops every cached scope of its instance, so no change can be
/// missed by a per-user key that was not derived (a role grant reaches many users). Access changes are rare.
/// </remarks>
internal static class RevocationKeys
{
    public static string For(string appInstanceId) => $"modulus:{appInstanceId}";
}
