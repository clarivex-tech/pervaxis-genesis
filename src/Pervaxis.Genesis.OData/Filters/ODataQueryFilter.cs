/*
 ************************************************************************
 * Copyright (C) 2026 Clarivex Technologies Private Limited
 * All Rights Reserved.
 *
 * NOTICE: All intellectual and technical concepts contained
 * herein are proprietary to Clarivex Technologies Private Limited
 * and may be covered by Indian and Foreign Patents,
 * patents in process, and are protected by trade secret or
 * copyright law. Dissemination of this information or reproduction
 * of this material is strictly forbidden unless prior written
 * permission is obtained from Clarivex Technologies Private Limited.
 *
 * Product:   Pervaxis Platform
 * Website:   https://clarivex.tech
 ************************************************************************
 */

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pervaxis.Genesis.OData.Options;
using Pervaxis.Genesis.OData.Services;

namespace Pervaxis.Genesis.OData.Filters;

/// <summary>
/// Action filter that parses, validates, and applies OData query options.
/// Handles query parsing, complexity validation, and error responses.
/// </summary>
internal sealed class ODataQueryFilter : IAsyncActionFilter
{
    private readonly ODataOptions _options;
    private readonly QueryValidator _validator;
    private readonly QueryComplexityCalculator _complexityCalculator;
    private readonly ILogger<ODataQueryFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ODataQueryFilter"/> class.
    /// </summary>
    public ODataQueryFilter(
        IOptions<ODataOptions> options,
        QueryValidator validator,
        QueryComplexityCalculator complexityCalculator,
        ILogger<ODataQueryFilter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(complexityCalculator);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _validator = validator;
        _complexityCalculator = complexityCalculator;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var query = httpContext.Request.Query;

        // Parse query options from the request
        var queryContext = ParseQueryOptions(query);

        // Resolve per-endpoint overrides from attribute
        var attribute = context.ActionDescriptor.EndpointMetadata
            .OfType<ODataQueryableAttribute>()
            .FirstOrDefault();

        var effectiveMaxTop = attribute?.MaxTop > 0 ? attribute.MaxTop : _options.MaxTop;
        var effectiveMaxExpand = attribute?.MaxExpandDepth >= 0 ? attribute.MaxExpandDepth : _options.MaxExpandDepth;
        var effectiveAllowedOptions = attribute?.AllowedQueryOptions ?? _options.AllowedQueryOptions;

        // Validate
        var validationResult = _validator.Validate(
            queryContext, _options, effectiveAllowedOptions, effectiveMaxTop, effectiveMaxExpand);

        if (!validationResult.IsValid)
        {
            _logger.LogWarning(
                "OData query validation failed: {ErrorCode} - {ErrorMessage}. Path={Path}, Query={Query}",
                validationResult.ErrorCode, validationResult.ErrorMessage,
                httpContext.Request.Path.Value, httpContext.Request.QueryString.Value);

            context.Result = CreateProblemResult(
                StatusCodes.Status400BadRequest,
                validationResult.ErrorCode!,
                validationResult.ErrorMessage!);
            return;
        }

        // Store query context for downstream use (e.g., by the controller or result formatters)
        httpContext.Items["Genesis.OData.QueryContext"] = queryContext;

        // Execute the action
        await next();
    }

    private ODataQueryContext ParseQueryOptions(IQueryCollection query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filter = query.ContainsKey("$filter") ? query["$filter"].ToString() : null;
        var orderBy = query.ContainsKey("$orderby") ? query["$orderby"].ToString() : null;
        var select = query.ContainsKey("$select")
            ? query["$select"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();
        var expand = query.ContainsKey("$expand")
            ? query["$expand"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
            : new List<string>();

        var topValue = _options.DefaultPageSize;
        var topParsed = false;
        if (query.ContainsKey("$top") && int.TryParse(query["$top"], out var parsedTop))
        {
            topValue = parsedTop;
            topParsed = true;
        }

        var skipValue = 0;
        var skipParsed = false;
        if (query.ContainsKey("$skip") && int.TryParse(query["$skip"], out var parsedSkip))
        {
            skipValue = parsedSkip;
            skipParsed = true;
        }

        var countRequested = query.ContainsKey("$count") &&
            string.Equals(query["$count"], "true", StringComparison.OrdinalIgnoreCase);

        var usedOptions = ODataQueryOptions.None;
        if (filter is not null)
        {
            usedOptions |= ODataQueryOptions.Filter;
        }

        if (orderBy is not null)
        {
            usedOptions |= ODataQueryOptions.OrderBy;
        }

        if (select.Count > 0)
        {
            usedOptions |= ODataQueryOptions.Select;
        }

        if (expand.Count > 0)
        {
            usedOptions |= ODataQueryOptions.Expand;
        }

        if (topParsed)
        {
            usedOptions |= ODataQueryOptions.Top;
        }

        if (skipParsed)
        {
            usedOptions |= ODataQueryOptions.Skip;
        }

        if (countRequested)
        {
            usedOptions |= ODataQueryOptions.Count;
        }

        // Compute filter condition count (rough: count logical operators + 1)
        var filterConditionCount = 0;
        var filterDepth = 0;
        if (filter is not null)
        {
            filterConditionCount = 1 + CountOccurrences(filter, " and ") + CountOccurrences(filter, " or ");
            filterDepth = CountParenthesisDepth(filter);
        }

        // Compute expand depth (rough: count nested expands via parentheses)
        var expandDepth = expand.Count > 0 ? 1 : 0;
        foreach (var expandItem in expand)
        {
            var depth = 1 + CountOccurrences(expandItem, "(");
            if (depth > expandDepth)
            {
                expandDepth = depth;
            }
        }

        return new ODataQueryContext
        {
            RawFilter = filter,
            RawOrderBy = orderBy,
            SelectProperties = select,
            ExpandProperties = expand,
            EffectiveTop = topValue,
            Skip = skipValue,
            CountRequested = countRequested,
            FilterConditionCount = filterConditionCount,
            ExpandDepth = expandDepth,
            OrderByPropertyCount = orderBy?.Split(',', StringSplitOptions.RemoveEmptyEntries).Length ?? 0,
            SelectPropertyCount = select.Count,
            FilterDepth = filterDepth,
            UsedOptions = usedOptions,
            RequestPath = string.Empty,
            RawQueryString = string.Empty
        };
    }

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }

    private static int CountParenthesisDepth(string source)
    {
        var maxDepth = 0;
        var currentDepth = 0;
        foreach (var c in source)
        {
            if (c == '(')
            {
                currentDepth++;
                if (currentDepth > maxDepth)
                {
                    maxDepth = currentDepth;
                }
            }
            else if (c == ')')
            {
                currentDepth--;
            }
        }
        return maxDepth;
    }

    private static ObjectResult CreateProblemResult(int statusCode, string errorCode, string detail)
    {
        var problemDetails = new ProblemDetails
        {
            Type = $"https://pervaxis.io/problems/odata/{errorCode.ToLowerInvariant()}",
            Title = "OData Query Error",
            Status = statusCode,
            Detail = detail
        };
        problemDetails.Extensions["errorCode"] = errorCode;

        return new ObjectResult(problemDetails)
        {
            StatusCode = statusCode,
            ContentTypes = { "application/problem+json" }
        };
    }
}
